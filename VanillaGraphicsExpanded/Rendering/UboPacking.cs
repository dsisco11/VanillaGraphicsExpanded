using System;
using System.Buffers.Binary;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Packs std140 components into caller-owned byte spans without tracking buffer lifetime.</summary>
internal static class UboPacking
{
    #region Public API
    /// <summary>Reports stored-bit changes when writing a four-byte float component at the caller's layout offset.</summary>
    public static bool WriteFloat(Span<byte> dst, int byteOffset, float value)
    {
        Span<byte> target = dst.Slice(byteOffset, 4);
        int bits = BitConverter.SingleToInt32Bits(value);
        if (BinaryPrimitives.ReadInt32LittleEndian(target) == bits) return false;
        BinaryPrimitives.WriteInt32LittleEndian(target, bits);
        return true;
    }

    /// <summary>Reports stored-bit changes when writing a four-byte int component at the caller's layout offset.</summary>
    public static bool WriteInt32(Span<byte> dst, int byteOffset, int value)
    {
        Span<byte> target = dst.Slice(byteOffset, 4);
        int bits = value;
        if (BinaryPrimitives.ReadInt32LittleEndian(target) == bits) return false;
        BinaryPrimitives.WriteInt32LittleEndian(target, bits);
        return true;
    }

    /// <summary>Reports stored-bit changes when writing a four-byte uint component at the caller's layout offset.</summary>
    public static bool WriteUInt32(Span<byte> dst, int byteOffset, uint value)
    {
        Span<byte> target = dst.Slice(byteOffset, 4);
        int bits = unchecked((int)value);
        if (BinaryPrimitives.ReadInt32LittleEndian(target) == bits) return false;
        BinaryPrimitives.WriteInt32LittleEndian(target, bits);
        return true;
    }

    /// <summary>Writes 2 float components and reports stored-bit changes without overwriting adjacent std140 padding or members.</summary>
    public static bool WriteVec2(Span<byte> dst, int byteOffset, float x, float y)
    {
        Span<byte> target = dst.Slice(byteOffset, 8);
        bool changed = false;
        changed |= WriteFloat(target, 0, x);
        changed |= WriteFloat(target, 4, y);
        return changed;
    }

    /// <summary>Writes 3 float components and reports stored-bit changes without overwriting adjacent std140 padding or members.</summary>
    public static bool WriteVec3(Span<byte> dst, int byteOffset, float x, float y, float z)
    {
        Span<byte> target = dst.Slice(byteOffset, 12);
        bool changed = false;
        changed |= WriteFloat(target, 0, x);
        changed |= WriteFloat(target, 4, y);
        changed |= WriteFloat(target, 8, z);
        return changed;
    }

    /// <summary>Packs std140 components and returns true only when their stored bits change.</summary>
    public static bool WriteMat4(Span<byte> dst, int byteOffset, ReadOnlySpan<float> m16)
    {
        if (m16.Length < 16) throw new ArgumentOutOfRangeException(nameof(m16), "Expected 16 floats for mat4.");

        // Validate the whole value before modifying any component; compare stored bits, not float equality.
        Span<byte> b = dst.Slice(byteOffset, 64);
        bool changed = false;

        // std140 mat4 is 4 contiguous vec4 columns (16 floats).
        for (int i = 0; i < 16; i++)
        {
            changed |= WriteFloat(b, i * 4, m16[i]);
        }
        return changed;
    }

    /// <summary>Packs std140 components and returns true only when their stored bits change.</summary>
    public static bool WriteVec4(Span<byte> dst, int byteOffset, float x, float y, float z, float w)
    {

        // Validate the whole value before modifying any component; compare stored bits, not float equality.
        Span<byte> b = dst.Slice(byteOffset, 16);
        bool changed = false;
        changed |= WriteFloat(b, 0, x);
        changed |= WriteFloat(b, 4, y);
        changed |= WriteFloat(b, 8, z);
        changed |= WriteFloat(b, 12, w);
        return changed;
    }

    /// <summary>Packs std140 components and returns true only when their stored bits change.</summary>
    public static bool WriteUVec4(Span<byte> dst, int byteOffset, uint x, uint y, uint z, uint w)
    {

        // Validate the whole value before modifying any component; compare stored bits, not float equality.
        Span<byte> b = dst.Slice(byteOffset, 16);
        bool changed = false;
        changed |= WriteUInt32(b, 0, x);
        changed |= WriteUInt32(b, 4, y);
        changed |= WriteUInt32(b, 8, z);
        changed |= WriteUInt32(b, 12, w);
        return changed;
    }

    /// <summary>Packs std140 components and returns true only when their stored bits change.</summary>
    public static bool WriteIVec4(Span<byte> dst, int byteOffset, int x, int y, int z, int w)
    {

        // Validate the whole value before modifying any component; compare stored bits, not float equality.
        Span<byte> b = dst.Slice(byteOffset, 16);
        bool changed = false;
        changed |= WriteInt32(b, 0, x);
        changed |= WriteInt32(b, 4, y);
        changed |= WriteInt32(b, 8, z);
        changed |= WriteInt32(b, 12, w);
        return changed;
    }

    // Read helpers for UBO property getters

    /// <summary>Reads std140 components at the supplied layout offset.</summary>
    public static float ReadFloat(ReadOnlySpan<byte> src, int byteOffset)
    {
        if ((uint)byteOffset > (uint)(src.Length - 4)) throw new ArgumentOutOfRangeException(nameof(byteOffset));
        return BinaryPrimitives.ReadSingleLittleEndian(src.Slice(byteOffset, 4));
    }

    /// <summary>Reads std140 components at the supplied layout offset.</summary>
    public static int ReadInt32(ReadOnlySpan<byte> src, int byteOffset)
    {
        if ((uint)byteOffset > (uint)(src.Length - 4)) throw new ArgumentOutOfRangeException(nameof(byteOffset));
        return BinaryPrimitives.ReadInt32LittleEndian(src.Slice(byteOffset, 4));
    }

    /// <summary>Reads std140 components at the supplied layout offset.</summary>
    public static uint ReadUInt32(ReadOnlySpan<byte> src, int byteOffset)
    {
        if ((uint)byteOffset > (uint)(src.Length - 4)) throw new ArgumentOutOfRangeException(nameof(byteOffset));
        return BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(byteOffset, 4));
    }

    /// <summary>Reads std140 components at the supplied layout offset.</summary>
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

    /// <summary>Reads std140 components at the supplied layout offset.</summary>
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

    /// <summary>Reads std140 components at the supplied layout offset.</summary>
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
    #endregion
}
