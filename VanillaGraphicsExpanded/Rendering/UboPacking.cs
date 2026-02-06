using System;
using System.Buffers.Binary;

namespace VanillaGraphicsExpanded.Rendering;

internal static class UboPacking
{
    public static void WriteMat4(Span<byte> dst, int byteOffset, ReadOnlySpan<float> m16)
    {
        if (m16.Length < 16) throw new ArgumentOutOfRangeException(nameof(m16), "Expected 16 floats for mat4.");
        if ((uint)byteOffset > (uint)(dst.Length - 64)) throw new ArgumentOutOfRangeException(nameof(byteOffset));

        Span<byte> b = dst.Slice(byteOffset, 64);

        // std140 mat4 is 4 contiguous vec4 columns (16 floats).
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(b.Slice(i * 4, 4), m16[i]);
        }
    }

    public static void WriteVec4(Span<byte> dst, int byteOffset, float x, float y, float z, float w)
    {
        if ((uint)byteOffset > (uint)(dst.Length - 16)) throw new ArgumentOutOfRangeException(nameof(byteOffset));

        Span<byte> b = dst.Slice(byteOffset, 16);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(0, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(4, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(8, 4), z);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(12, 4), w);
    }

    public static void WriteUVec4(Span<byte> dst, int byteOffset, uint x, uint y, uint z, uint w)
    {
        if ((uint)byteOffset > (uint)(dst.Length - 16)) throw new ArgumentOutOfRangeException(nameof(byteOffset));

        Span<byte> b = dst.Slice(byteOffset, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(0, 4), x);
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(4, 4), y);
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(8, 4), z);
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(12, 4), w);
    }

    public static void WriteIVec4(Span<byte> dst, int byteOffset, int x, int y, int z, int w)
    {
        if ((uint)byteOffset > (uint)(dst.Length - 16)) throw new ArgumentOutOfRangeException(nameof(byteOffset));

        Span<byte> b = dst.Slice(byteOffset, 16);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(0, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(4, 4), y);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(8, 4), z);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(12, 4), w);
    }

    // Read helpers for UBO property getters

    public static float ReadFloat(ReadOnlySpan<byte> src, int byteOffset)
    {
        if ((uint)byteOffset > (uint)(src.Length - 4)) throw new ArgumentOutOfRangeException(nameof(byteOffset));
        return BinaryPrimitives.ReadSingleLittleEndian(src.Slice(byteOffset, 4));
    }

    public static int ReadInt32(ReadOnlySpan<byte> src, int byteOffset)
    {
        if ((uint)byteOffset > (uint)(src.Length - 4)) throw new ArgumentOutOfRangeException(nameof(byteOffset));
        return BinaryPrimitives.ReadInt32LittleEndian(src.Slice(byteOffset, 4));
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> src, int byteOffset)
    {
        if ((uint)byteOffset > (uint)(src.Length - 4)) throw new ArgumentOutOfRangeException(nameof(byteOffset));
        return BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(byteOffset, 4));
    }

    public static (float x, float y, float z, float w) ReadVec4(ReadOnlySpan<byte> src, int byteOffset)
    {
        if ((uint)byteOffset > (uint)(src.Length - 16)) throw new ArgumentOutOfRangeException(nameof(byteOffset));

        ReadOnlySpan<byte> b = src.Slice(byteOffset, 16);
        return (
            BinaryPrimitives.ReadSingleLittleEndian(b.Slice(0, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(b.Slice(4, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(b.Slice(8, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(b.Slice(12, 4))
        );
    }

    public static (int x, int y, int z, int w) ReadIVec4(ReadOnlySpan<byte> src, int byteOffset)
    {
        if ((uint)byteOffset > (uint)(src.Length - 16)) throw new ArgumentOutOfRangeException(nameof(byteOffset));

        ReadOnlySpan<byte> b = src.Slice(byteOffset, 16);
        return (
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(0, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(4, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(8, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(12, 4))
        );
    }

    public static (uint x, uint y, uint z, uint w) ReadUVec4(ReadOnlySpan<byte> src, int byteOffset)
    {
        if ((uint)byteOffset > (uint)(src.Length - 16)) throw new ArgumentOutOfRangeException(nameof(byteOffset));

        ReadOnlySpan<byte> b = src.Slice(byteOffset, 16);
        return (
            BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(0, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(4, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(12, 4))
        );
    }
}
