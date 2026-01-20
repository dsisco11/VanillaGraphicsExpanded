using System;
using System.Buffers.Binary;
using System.IO;

using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal static class DdsBc7Rgba8Codec
{
    private const uint DdsMagic = 0x20534444; // "DDS "
    private const int DdsHeaderSize = 124;
    private const int DdsPixelFormatSize = 32;

    private const uint DdpfFourCc = 0x4;
    private const uint FourCcDx10 = 0x30315844; // "DX10"

    // DXGI_FORMAT_BC7_UNORM / DXGI_FORMAT_BC7_UNORM_SRGB
    private const uint DxgiFormatBc7Unorm = 98;
    private const uint DxgiFormatBc7UnormSrgb = 99;

    // D3D10_RESOURCE_DIMENSION_TEXTURE2D
    private const uint ResourceDimensionTexture2D = 3;

    // DDS_HEADER layout: DDS_PIXELFORMAT starts at offset 72.
    private const int DdsPixelFormatOffset = 72;

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

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        WriteBc7Dds(fs, width, height, rgba);
        fs.Flush(flushToDisk: true);
    }

    public static void WriteBc7Dds(Stream stream, int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        int expected = checked(width * height * 4);
        if (rgba.Length != expected)
        {
            throw new ArgumentException($"Expected rgba length {expected}, got {rgba.Length}.", nameof(rgba));
        }

        var encoder = new BcEncoder();
        encoder.OutputOptions.Format = CompressionFormat.Bc7;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.DdsPreferDxt10Header = true;

        DdsFile dds = encoder.EncodeToDds(rgba, width, height, PixelFormat.Rgba32);
        dds.Write(stream);
    }

    public static byte[] ReadRgba8FromDds(string filePath, out int width, out int height)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadRgba8FromDds(fs, out width, out height);
    }

    public static byte[] ReadRgba8FromDds(Stream stream, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var decoder = new BcDecoder();
        var pixels2d = decoder.Decode2D(stream);

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

    public static void ReadBc7Header(string filePath, out int width, out int height)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        ReadBc7Header(fs, out width, out height);
    }

    public static void ReadBc7Header(Stream stream, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Span<byte> prefix = stackalloc byte[4 + DdsHeaderSize + 20];
        stream.ReadExactly(prefix);

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(prefix[0..4]);
        if (magic != DdsMagic)
        {
            throw new InvalidDataException("DDS magic mismatch.");
        }

        Span<byte> h = prefix.Slice(4, DdsHeaderSize);

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(h[0..4]);
        if (headerSize != DdsHeaderSize)
        {
            throw new InvalidDataException($"Unsupported DDS header size {headerSize}.");
        }

        height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(h[8..12]));
        width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(h[12..16]));

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("DDS had invalid dimensions.");
        }

        Span<byte> pf = h.Slice(DdsPixelFormatOffset, DdsPixelFormatSize);
        uint pfSize = BinaryPrimitives.ReadUInt32LittleEndian(pf[0..4]);
        uint pfFlags = BinaryPrimitives.ReadUInt32LittleEndian(pf[4..8]);
        uint fourCc = BinaryPrimitives.ReadUInt32LittleEndian(pf[8..12]);

        if (pfSize != DdsPixelFormatSize || (pfFlags & DdpfFourCc) == 0 || fourCc != FourCcDx10)
        {
            throw new InvalidDataException("DDS pixel format is not DX10.");
        }

        Span<byte> dx10 = prefix.Slice(4 + DdsHeaderSize, 20);
        uint dxgiFormat = BinaryPrimitives.ReadUInt32LittleEndian(dx10[0..4]);
        uint dimension = BinaryPrimitives.ReadUInt32LittleEndian(dx10[4..8]);
        uint arraySize = BinaryPrimitives.ReadUInt32LittleEndian(dx10[12..16]);

        bool isBc7 = dxgiFormat == DxgiFormatBc7Unorm || dxgiFormat == DxgiFormatBc7UnormSrgb;
        if (!isBc7 || dimension != ResourceDimensionTexture2D || arraySize != 1)
        {
            throw new InvalidDataException("DDS DX10 header does not describe a 2D BC7 texture.");
        }
    }
}
