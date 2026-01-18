using System;
using System.Buffers.Binary;
using System.IO;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal static class DdsRgba16fCodec
{
    private const uint DdsMagic = 0x20534444; // "DDS "
    private const int DdsHeaderSize = 124;
    private const int DdsPixelFormatSize = 32;

    private const uint DdsdCaps = 0x1;
    private const uint DdsdHeight = 0x2;
    private const uint DdsdWidth = 0x4;
    private const uint DdsdPitch = 0x8;
    private const uint DdsdPixelFormat = 0x1000;

    private const uint DdpfFourCc = 0x4;
    private const uint FourCcDx10 = 0x30315844; // "DX10"

    private const uint DdsCapsTexture = 0x1000;

    // DXGI_FORMAT_R16G16B16A16_FLOAT
    private const uint DxgiFormatR16G16B16A16Float = 10;

    // D3D10_RESOURCE_DIMENSION_TEXTURE2D
    private const uint ResourceDimensionTexture2D = 3;

    public static void WriteRgba16f(string filePath, int width, int height, ReadOnlySpan<float> rgba)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        int expectedFloats = checked(width * height * 4);
        if (rgba.Length != expectedFloats)
        {
            throw new ArgumentException($"Expected rgba length {expectedFloats}, got {rgba.Length}.", nameof(rgba));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

        Span<byte> header = stackalloc byte[4 + DdsHeaderSize + 20];
        header.Clear();

        // Magic
        BinaryPrimitives.WriteUInt32LittleEndian(header[0..4], DdsMagic);

        // DDS_HEADER
        Span<byte> h = header.Slice(4, DdsHeaderSize);

        BinaryPrimitives.WriteUInt32LittleEndian(h[0..4], DdsHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h[4..8], DdsdCaps | DdsdHeight | DdsdWidth | DdsdPixelFormat | DdsdPitch);
        BinaryPrimitives.WriteUInt32LittleEndian(h[8..12], (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(h[12..16], (uint)width);

        uint pitchBytes = checked((uint)width * 8u);
        BinaryPrimitives.WriteUInt32LittleEndian(h[16..20], pitchBytes);

        // ddspf at offset 76
        Span<byte> pf = h.Slice(76, DdsPixelFormatSize);
        BinaryPrimitives.WriteUInt32LittleEndian(pf[0..4], DdsPixelFormatSize);
        BinaryPrimitives.WriteUInt32LittleEndian(pf[4..8], DdpfFourCc);
        BinaryPrimitives.WriteUInt32LittleEndian(pf[8..12], FourCcDx10);

        // caps
        BinaryPrimitives.WriteUInt32LittleEndian(h[108..112], DdsCapsTexture);

        // DDS_HEADER_DX10 (20 bytes)
        Span<byte> dx10 = header.Slice(4 + DdsHeaderSize, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(dx10[0..4], DxgiFormatR16G16B16A16Float);
        BinaryPrimitives.WriteUInt32LittleEndian(dx10[4..8], ResourceDimensionTexture2D);
        BinaryPrimitives.WriteUInt32LittleEndian(dx10[8..12], 0u); // miscFlag
        BinaryPrimitives.WriteUInt32LittleEndian(dx10[12..16], 1u); // arraySize
        BinaryPrimitives.WriteUInt32LittleEndian(dx10[16..20], 0u); // miscFlags2

        fs.Write(header);

        // Payload: Half RGBA (little endian)
        Span<byte> outBytes = stackalloc byte[8 * 256]; // 256 pixels at a time
        int floatsPerChunk = outBytes.Length / 2; // each half is 2 bytes

        int i = 0;
        while (i < rgba.Length)
        {
            int floatsThis = Math.Min(floatsPerChunk, rgba.Length - i);
            int bytesThis = floatsThis * 2;

            Span<byte> buf = outBytes.Slice(0, bytesThis);

            int bi = 0;
            for (int j = 0; j < floatsThis; j++)
            {
                Half half = (Half)rgba[i + j];
                ushort bits = BitConverter.HalfToUInt16Bits(half);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(bi, 2), bits);
                bi += 2;
            }

            fs.Write(buf);
            i += floatsThis;
        }

        fs.Flush(flushToDisk: true);
    }

    public static float[] ReadRgba16f(string filePath, out int width, out int height)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        Span<byte> prefix = stackalloc byte[4 + DdsHeaderSize + 20];
        fs.ReadExactly(prefix);

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

        Span<byte> pf = h.Slice(76, DdsPixelFormatSize);
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

        if (dxgiFormat != DxgiFormatR16G16B16A16Float || dimension != ResourceDimensionTexture2D || arraySize != 1)
        {
            throw new InvalidDataException("DDS DX10 header does not describe a 2D R16G16B16A16_FLOAT texture.");
        }

        int floats = checked(width * height * 4);
        int bytes = checked(width * height * 8);

        byte[] payload = new byte[bytes];
        fs.ReadExactly(payload);

        var rgba = new float[floats];
        int pi = 0;

        for (int fi = 0; fi < floats; fi++)
        {
            ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(pi, 2));
            Half half = BitConverter.UInt16BitsToHalf(bits);
            rgba[fi] = (float)half;
            pi += 2;
        }

        return rgba;
    }
}
