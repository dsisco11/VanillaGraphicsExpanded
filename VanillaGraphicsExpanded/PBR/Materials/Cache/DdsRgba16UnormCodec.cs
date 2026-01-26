using System;
using System.Buffers.Binary;
using System.IO;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal static class DdsRgba16UnormCodec
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

    // DDS_HEADER layout: DDS_PIXELFORMAT starts at offset 72.
    private const int DdsPixelFormatOffset = 72;
    private const int DdsCapsOffset = 104;

    // DXGI_FORMAT_R16G16B16A16_UNORM
    private const uint DxgiFormatR16G16B16A16Unorm = 11;

    // D3D10_RESOURCE_DIMENSION_TEXTURE2D
    private const uint ResourceDimensionTexture2D = 3;

    public static void WriteRgba16Unorm(string filePath, int width, int height, ReadOnlySpan<ushort> rgba)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        int expectedU16 = checked(width * height * 4);
        if (rgba.Length != expectedU16)
        {
            throw new ArgumentException($"Expected rgba length {expectedU16}, got {rgba.Length}.", nameof(rgba));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        WriteRgba16Unorm(fs, width, height, rgba);
        fs.Flush(flushToDisk: true);
    }

    public static void WriteRgba16Unorm(Stream stream, int width, int height, ReadOnlySpan<ushort> rgba)
    {
        ArgumentNullException.ThrowIfNull(stream);

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

        // ddspf at offset 72
        Span<byte> pf = h.Slice(DdsPixelFormatOffset, DdsPixelFormatSize);
        BinaryPrimitives.WriteUInt32LittleEndian(pf[0..4], DdsPixelFormatSize);
        BinaryPrimitives.WriteUInt32LittleEndian(pf[4..8], DdpfFourCc);
        BinaryPrimitives.WriteUInt32LittleEndian(pf[8..12], FourCcDx10);

        // caps
        BinaryPrimitives.WriteUInt32LittleEndian(h[DdsCapsOffset..(DdsCapsOffset + 4)], DdsCapsTexture);

        // DDS_HEADER_DX10 (20 bytes)
        Span<byte> dx10 = header.Slice(4 + DdsHeaderSize, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(dx10[0..4], DxgiFormatR16G16B16A16Unorm);
        BinaryPrimitives.WriteUInt32LittleEndian(dx10[4..8], ResourceDimensionTexture2D);
        BinaryPrimitives.WriteUInt32LittleEndian(dx10[8..12], 0u); // miscFlag
        BinaryPrimitives.WriteUInt32LittleEndian(dx10[12..16], 1u); // arraySize
        BinaryPrimitives.WriteUInt32LittleEndian(dx10[16..20], 0u); // miscFlags2

        stream.Write(header);

        Span<byte> outBytes = stackalloc byte[8 * 256]; // 256 pixels at a time
        int u16PerChunk = outBytes.Length / 2;

        int i = 0;
        while (i < rgba.Length)
        {
            int u16This = Math.Min(u16PerChunk, rgba.Length - i);
            int bytesThis = u16This * 2;

            Span<byte> buf = outBytes.Slice(0, bytesThis);

            int bi = 0;
            for (int j = 0; j < u16This; j++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(bi, 2), rgba[i + j]);
                bi += 2;
            }

            stream.Write(buf);
            i += u16This;
        }
    }

    public static ushort[] ReadRgba16Unorm(string filePath, out int width, out int height)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadRgba16Unorm(fs, out width, out height);
    }

    public static void ReadRgba16UnormHeader(string filePath, out int width, out int height)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        ReadRgba16UnormHeader(fs, out width, out height);
    }

    public static void ReadRgba16UnormHeader(Stream stream, out int width, out int height)
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

        if (dxgiFormat != DxgiFormatR16G16B16A16Unorm || dimension != ResourceDimensionTexture2D || arraySize != 1)
        {
            throw new InvalidDataException("DDS DX10 header does not describe a 2D R16G16B16A16_UNORM texture.");
        }
    }

    public static ushort[] ReadRgba16Unorm(Stream stream, out int width, out int height)
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

        Span<byte> pf = h.Slice(76, DdsPixelFormatSize);
        // NOTE: DDS_PIXELFORMAT starts at offset 72 (see DdsPixelFormatOffset).
        pf = h.Slice(DdsPixelFormatOffset, DdsPixelFormatSize);
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

        if (dxgiFormat != DxgiFormatR16G16B16A16Unorm || dimension != ResourceDimensionTexture2D || arraySize != 1)
        {
            throw new InvalidDataException("DDS DX10 header does not describe a 2D R16G16B16A16_UNORM texture.");
        }

        int u16 = checked(width * height * 4);
        int bytes = checked(width * height * 8);

        byte[] payload = new byte[bytes];
        stream.ReadExactly(payload);

        var rgba = new ushort[u16];
        int pi = 0;

        for (int ui = 0; ui < u16; ui++)
        {
            rgba[ui] = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(pi, 2));
            pi += 2;
        }

        return rgba;
    }
}
