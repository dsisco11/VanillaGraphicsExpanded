using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using BCnEncoder.Decoder;
using BCnEncoder.Shared;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal sealed class MaterialOverrideTextureLoader
{
    internal enum PixelDataFormat : byte
    {
        Rgba8Unorm = 1,
        Rgba16Unorm = 2,
    }

    internal readonly record struct LoadedRgbaImage(int Width, int Height, PixelDataFormat Format, byte[] Bytes)
    {
        public bool IsEmpty => Width <= 0 || Height <= 0 || Bytes is null || Bytes.Length == 0;
    }

    private sealed class CacheEntry
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required PixelDataFormat Format { get; init; }
        public required byte[] Bytes { get; init; }
        public float[]? RgbaFloats01 { get; set; }
        public DateTime LastAccessUtc { get; set; }
    }

    private const float InvU16Max = 1f / 65535f;

    private const int MaxCacheEntries = 256;
    private static readonly object CacheGate = new();
    private static readonly Dictionary<AssetLocation, CacheEntry> Cache = new();

    public void ClearCache()
    {
        lock (CacheGate)
        {
            Cache.Clear();
        }
    }

    private static bool TryLoadRgba(
        ICoreClientAPI capi,
        AssetLocation overrideId,
        out LoadedRgbaImage image,
        out string? reason,
        int? expectedWidth = null,
        int? expectedHeight = null)
    {
        ArgumentNullException.ThrowIfNull(capi);
        ArgumentNullException.ThrowIfNull(overrideId);

        image = default;
        reason = null;

        // Fast path: serve from cache if available (path equality).
        if (TryGetCachedBytes(overrideId, expectedWidth, expectedHeight, out image))
        {
            return true;
        }

        IAsset? asset = capi.Assets.TryGet(overrideId, loadAsset: true);
        if (asset == null)
        {
            reason = "asset not found";
            return false;
        }

        string path = (overrideId.Path ?? string.Empty).Replace('\\', '/').ToLowerInvariant();

        try
        {
            if (path.EndsWith(".png", StringComparison.Ordinal))
            {
                if (!TryLoadPng(capi, asset, out image, out reason, expectedWidth, expectedHeight))
                {
                    return false;
                }

                UpsertCache(overrideId, image);
                return true;
            }

            if (path.EndsWith(".dds", StringComparison.Ordinal))
            {
                if (!TryLoadDds(asset, out image, out reason, expectedWidth, expectedHeight))
                {
                    return false;
                }

                UpsertCache(overrideId, image);
                return true;
            }

            reason = "unsupported extension";
            return false;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public bool TryLoadRgbaFloats01(
        ICoreClientAPI capi,
        AssetLocation overrideId,
        out int width,
        out int height,
        out float[] rgba01,
        out string? reason,
        int? expectedWidth = null,
        int? expectedHeight = null)
    {
        width = 0;
        height = 0;
        rgba01 = Array.Empty<float>();
        reason = null;

        if (!TryLoadRgba(capi, overrideId, out LoadedRgbaImage img, out reason, expectedWidth, expectedHeight))
        {
            return false;
        }

        width = img.Width;
        height = img.Height;

        // If the underlying TryLoadRgba produced a cached image, convert-and-cache floats once.
        if (TryGetCachedFloats01(overrideId, expectedWidth, expectedHeight, out width, out height, out rgba01))
        {
            return true;
        }

        // Fallback: convert without caching.
        rgba01 = ConvertToFloats01(img.Format, img.Bytes);
        return true;
    }

    private static bool TryLoadPng(
        ICoreClientAPI capi,
        IAsset asset,
        out LoadedRgbaImage image,
        out string? reason,
        int? expectedWidth,
        int? expectedHeight)
    {
        image = default;
        reason = null;

        using BitmapRef bmp = asset.ToBitmap(capi);

        int width = bmp.Width;
        int height = bmp.Height;

        if (!ValidateDimensions(width, height, expectedWidth, expectedHeight, out reason))
        {
            return false;
        }

        int[] src = bmp.Pixels;
        if (src is null || src.Length < width * height)
        {
            reason = "png decode returned insufficient pixel data";
            return false;
        }

        byte[] rgba = new byte[width * height * 4];
        int di = 0;

        // Vintagestory bitmap pixels are stored as ARGB (A in the highest byte).
        for (int i = 0; i < width * height; i++)
        {
            int argb = src[i];
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);

            rgba[di++] = r;
            rgba[di++] = g;
            rgba[di++] = b;
            rgba[di++] = a;
        }

        image = new LoadedRgbaImage(width, height, PixelDataFormat.Rgba8Unorm, rgba);
        return true;
    }

    private static bool TryLoadDds(
        IAsset asset,
        out LoadedRgbaImage image,
        out string? reason,
        int? expectedWidth,
        int? expectedHeight)
    {
        image = default;
        reason = null;

        if (asset.Data is null || asset.Data.Length == 0)
        {
            reason = "dds asset had no data";
            return false;
        }

        return TryDecodeDdsBytes(asset.Data, out image, out reason, expectedWidth, expectedHeight);
    }

    internal static bool TryDecodeDdsBytes(
        byte[] ddsBytes,
        out LoadedRgbaImage image,
        out string? reason,
        int? expectedWidth,
        int? expectedHeight)
    {
        image = default;
        reason = null;

        if (ddsBytes is null || ddsBytes.Length == 0)
        {
            reason = "dds asset had no data";
            return false;
        }

        // Path A: BCnEncoder decode (BC1/BC3/BC5/BC7, etc.)
        try
        {
            using var ms = new MemoryStream(ddsBytes, writable: false);

            var decoder = new BcDecoder();
            var pixels2d = decoder.Decode2D(ms);

            int width = pixels2d.Width;
            int height = pixels2d.Height;

            if (!ValidateDimensions(width, height, expectedWidth, expectedHeight, out reason))
            {
                return false;
            }

            byte[] rgba = new byte[width * height * 4];
            int di = 0;

            foreach (ColorRgba32 px in pixels2d.Span)
            {
                rgba[di++] = px.r;
                rgba[di++] = px.g;
                rgba[di++] = px.b;
                rgba[di++] = px.a;
            }

            image = new LoadedRgbaImage(width, height, PixelDataFormat.Rgba8Unorm, rgba);
            return true;
        }
        catch
        {
            // Fall through to uncompressed DX10 decode.
        }

        // Path B: DX10 uncompressed R16G16B16A16_UNORM (used by some authoring tools).
        try
        {
            using var ms = new MemoryStream(ddsBytes, writable: false);
            ushort[] rgbaU16 = DdsRgba16UnormCodec.ReadRgba16Unorm(ms, out int width, out int height);

            if (!ValidateDimensions(width, height, expectedWidth, expectedHeight, out reason))
            {
                return false;
            }

            // Preserve full 16-bit precision by storing the raw bytes.
            // Note: on all supported runtimes/platforms here, ushort[] is stored little-endian in memory.
            // Buffer.BlockCopy is much faster than per-element writes.
            byte[] bytes = new byte[checked(rgbaU16.Length * 2)];
            Buffer.BlockCopy(rgbaU16, 0, bytes, 0, bytes.Length);

            image = new LoadedRgbaImage(width, height, PixelDataFormat.Rgba16Unorm, bytes);
            return true;
        }
        catch (Exception ex)
        {
            reason = $"dds decode failed: {ex.Message}";
            return false;
        }
    }

    private static bool ValidateDimensions(int width, int height, int? expectedWidth, int? expectedHeight, out string? reason)
    {
        reason = null;

        if (width <= 0 || height <= 0)
        {
            reason = "image had invalid dimensions";
            return false;
        }

        if (expectedWidth is not null && expectedHeight is not null)
        {
            if (width != expectedWidth.Value || height != expectedHeight.Value)
            {
                reason = $"dimension mismatch (expected {expectedWidth.Value}x{expectedHeight.Value}, got {width}x{height})";
                return false;
            }
        }

        return true;
    }

    private static bool TryGetCachedBytes(AssetLocation overrideId, int? expectedWidth, int? expectedHeight, out LoadedRgbaImage image)
    {
        image = default;

        lock (CacheGate)
        {
            if (!Cache.TryGetValue(overrideId, out CacheEntry? entry))
            {
                return false;
            }

            if (expectedWidth is not null && expectedHeight is not null &&
                (entry.Width != expectedWidth.Value || entry.Height != expectedHeight.Value))
            {
                return false;
            }

            entry.LastAccessUtc = DateTime.UtcNow;
            image = new LoadedRgbaImage(entry.Width, entry.Height, entry.Format, entry.Bytes);
            return true;
        }
    }

    private static bool TryGetCachedFloats01(
        AssetLocation overrideId,
        int? expectedWidth,
        int? expectedHeight,
        out int width,
        out int height,
        out float[] rgba01)
    {
        lock (CacheGate)
        {
            if (!Cache.TryGetValue(overrideId, out CacheEntry? entry))
            {
                width = 0;
                height = 0;
                rgba01 = Array.Empty<float>();
                return false;
            }

            if (expectedWidth is not null && expectedHeight is not null &&
                (entry.Width != expectedWidth.Value || entry.Height != expectedHeight.Value))
            {
                width = 0;
                height = 0;
                rgba01 = Array.Empty<float>();
                return false;
            }

            entry.LastAccessUtc = DateTime.UtcNow;
            entry.RgbaFloats01 ??= ConvertToFloats01(entry.Format, entry.Bytes);

            width = entry.Width;
            height = entry.Height;
            rgba01 = entry.RgbaFloats01;
            return true;
        }
    }

    private static void UpsertCache(AssetLocation overrideId, LoadedRgbaImage image)
    {
        lock (CacheGate)
        {
            Cache[overrideId] = new CacheEntry
            {
                Width = image.Width,
                Height = image.Height,
                Format = image.Format,
                Bytes = image.Bytes,
                LastAccessUtc = DateTime.UtcNow
            };

            if (Cache.Count <= MaxCacheEntries)
            {
                return;
            }

            // Evict least-recently-used entries.
            foreach (AssetLocation key in Cache
                .OrderBy(kvp => kvp.Value.LastAccessUtc)
                .Take(Cache.Count - MaxCacheEntries)
                .Select(kvp => kvp.Key)
                .ToArray())
            {
                Cache.Remove(key);
            }
        }
    }

    private static float[] ConvertToFloats01(PixelDataFormat format, byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return Array.Empty<float>();
        }

        if (format == PixelDataFormat.Rgba8Unorm)
        {
            var floats = new float[bytes.Length];
            SimdSpanMath.BytesToSingles01(bytes, floats);
            return floats;
        }

        if (format == PixelDataFormat.Rgba16Unorm)
        {
            if ((bytes.Length & 1) != 0)
            {
                throw new InvalidDataException("RGBA16_UNORM byte payload must have even length.");
            }

            int u16 = bytes.Length / 2;
            var floats = new float[u16];

            for (int i = 0; i < u16; i++)
            {
                ushort v = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2));
                floats[i] = v * InvU16Max;
            }

            return floats;
        }

        throw new InvalidDataException($"Unknown pixel format {format}.");
    }

}
