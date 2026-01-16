using System;
using System.IO;

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
                return TryLoadPng(capi, asset, out image, out reason, expectedWidth, expectedHeight);
            }

            if (path.EndsWith(".dds", StringComparison.Ordinal))
            {
                return TryLoadDds(asset, out image, out reason, expectedWidth, expectedHeight);
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
}
