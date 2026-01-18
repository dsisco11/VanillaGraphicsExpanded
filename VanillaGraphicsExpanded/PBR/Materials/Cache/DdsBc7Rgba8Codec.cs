using System;
using System.IO;

using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal static class DdsBc7Rgba8Codec
{
    public static void WriteBc7Dds(string filePath, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        int expected = checked(width * height * 4);
        if (rgba.Length != expected)
        {
            throw new ArgumentException($"Expected rgba length {expected}, got {rgba.Length}.", nameof(rgba));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var encoder = new BcEncoder();
        encoder.OutputOptions.Format = CompressionFormat.Bc7;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.DdsPreferDxt10Header = true;

        DdsFile dds = encoder.EncodeToDds(rgba, width, height, PixelFormat.Rgba32);

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        dds.Write(fs);
        fs.Flush(flushToDisk: true);
    }

    public static byte[] ReadRgba8FromDds(string filePath, out int width, out int height)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var decoder = new BcDecoder();
        var pixels2d = decoder.Decode2D(fs);

        width = pixels2d.Width;
        height = pixels2d.Height;

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("DDS had invalid dimensions.");
        }

        byte[] rgba = new byte[checked(width * height * 4)];
        int di = 0;

        foreach (ColorRgba32 px in pixels2d.Span)
        {
            rgba[di++] = px.r;
            rgba[di++] = px.g;
            rgba[di++] = px.b;
            rgba[di++] = px.a;
        }

        return rgba;
    }
}
