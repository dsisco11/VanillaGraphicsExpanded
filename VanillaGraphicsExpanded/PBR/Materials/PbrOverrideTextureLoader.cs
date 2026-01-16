using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using BCnEncoder.Decoder;
using BCnEncoder.Shared;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal static class PbrOverrideTextureLoader
{
    internal readonly record struct LoadedRgbaImage(int Width, int Height, byte[] Rgba)
    {
        public bool IsEmpty => Width <= 0 || Height <= 0 || Rgba is null || Rgba.Length == 0;
    }

    private sealed class CacheEntry
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required byte[] RgbaBytes { get; init; }
        public float[]? RgbaFloats01 { get; set; }
        public DateTime LastAccessUtc { get; set; }
    }

    private const int MaxCacheEntries = 256;
    private static readonly object CacheGate = new();
    private static readonly Dictionary<AssetLocation, CacheEntry> Cache = new();

    public static void ClearCache()
    {
        lock (CacheGate)
        {
            Cache.Clear();
        }
    }

    public static bool TryLoadRgba(
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

                UpsertCache(overrideId, image.Width, image.Height, image.Rgba);
                return true;
            }

            if (path.EndsWith(".dds", StringComparison.Ordinal))
            {
                if (!TryLoadDds(asset, out image, out reason, expectedWidth, expectedHeight))
                {
                    return false;
                }

                UpsertCache(overrideId, image.Width, image.Height, image.Rgba);
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

    public static bool TryLoadRgbaFloats01(
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
        rgba01 = ConvertBytesToFloats01(img.Rgba);
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

        image = new LoadedRgbaImage(width, height, rgba);
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

        try
        {
            using var ms = new MemoryStream(asset.Data, writable: false);

            // Core BCnEncoder API (no ImageSharp dependency): decode to raw RGBA memory.
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

            // ColorRgba32 is expected to be 8-bit per channel.
            foreach (ColorRgba32 px in pixels2d.Span)
            {
                rgba[di++] = px.r;
                rgba[di++] = px.g;
                rgba[di++] = px.b;
                rgba[di++] = px.a;
            }

            image = new LoadedRgbaImage(width, height, rgba);
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
            image = new LoadedRgbaImage(entry.Width, entry.Height, entry.RgbaBytes);
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
            entry.RgbaFloats01 ??= ConvertBytesToFloats01(entry.RgbaBytes);

            width = entry.Width;
            height = entry.Height;
            rgba01 = entry.RgbaFloats01;
            return true;
        }
    }

    private static void UpsertCache(AssetLocation overrideId, int width, int height, byte[] rgba)
    {
        lock (CacheGate)
        {
            Cache[overrideId] = new CacheEntry
            {
                Width = width,
                Height = height,
                RgbaBytes = rgba,
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

    private static float[] ConvertBytesToFloats01(byte[] rgba)
    {
        var floats = new float[rgba.Length];
        for (int i = 0; i < rgba.Length; i++)
        {
            floats[i] = rgba[i] / 255f;
        }

        return floats;
    }

}
