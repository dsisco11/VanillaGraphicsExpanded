using System.Buffers.Binary;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies representation comparisons and atomic destination validation for every packing family.</summary>
public sealed class UboPackingTests
{
    #region Public API
    /// <summary>Every writer detects identical data while preserving bytes outside its occupied range.</summary>
    [Theory]
    [InlineData(0, 4)] [InlineData(1, 4)] [InlineData(2, 4)]
    [InlineData(3, 8)] [InlineData(4, 12)] [InlineData(5, 16)]
    [InlineData(6, 16)] [InlineData(7, 16)] [InlineData(8, 64)]
    public void WritersReportChangesAndPreserveNeighbors(int writer, int width)
    {
        byte[] bytes = Enumerable.Repeat((byte)0xCD, 80).ToArray();
        Assert.True(Write(writer, bytes, 4));
        byte[] stored = bytes.ToArray();
        Assert.False(Write(writer, bytes, 4));
        Assert.Equal(stored, bytes);
        Assert.All(bytes[..4], value => Assert.Equal((byte)0xCD, value));
        Assert.All(bytes[(4 + width)..], value => Assert.Equal((byte)0xCD, value));
        bytes[4 + width - 1] ^= 1;
        Assert.True(Write(writer, bytes, 4));
        Assert.Equal(stored, bytes);
    }

    /// <summary>Short, negative and overflow-sized ranges fail before any destination bytes change.</summary>
    [Theory]
    [InlineData(0, 4)] [InlineData(1, 4)] [InlineData(2, 4)]
    [InlineData(3, 8)] [InlineData(4, 12)] [InlineData(5, 16)]
    [InlineData(6, 16)] [InlineData(7, 16)] [InlineData(8, 64)]
    public void InvalidRangesNeverPartiallyWrite(int writer, int width)
    {
        foreach (int offset in new[] { -1, 1, int.MaxValue })
        {
            byte[] bytes = Enumerable.Repeat((byte)0xCD, width).ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() => Write(writer, bytes, offset));
            Assert.All(bytes, value => Assert.Equal((byte)0xCD, value));
        }
        byte[] shortBytes = Enumerable.Repeat((byte)0xCD, width - 1).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => Write(writer, shortBytes, 0));
        Assert.All(shortBytes, value => Assert.Equal((byte)0xCD, value));
    }

    /// <summary>Signed zero and distinct NaN payloads compare by their stored bits.</summary>
    [Fact]
    public void FloatingPatternsRetainSignedZeroAndNanPayloads()
    {
        byte[] bytes = new byte[64];
        Assert.False(UboPacking.WriteFloat(bytes, 0, 0f));
        float negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        Assert.True(UboPacking.WriteFloat(bytes, 0, negativeZero));
        Assert.False(UboPacking.WriteFloat(bytes, 0, negativeZero));
        foreach (int bits in new[] { 0x7FC00001, 0x7FC00002, unchecked((int)0xFFC00002) })
        {
            float nan = BitConverter.Int32BitsToSingle(bits);
            Assert.True(UboPacking.WriteVec4(bytes, 0, nan, negativeZero, nan, 0));
            Assert.False(UboPacking.WriteVec4(bytes, 0, nan, negativeZero, nan, 0));
            Assert.Equal(bits, BinaryPrimitives.ReadInt32LittleEndian(bytes));
            float[] matrix = Enumerable.Repeat(nan, 16).ToArray();
            Assert.True(UboPacking.WriteMat4(bytes, 0, matrix));
            Assert.False(UboPacking.WriteMat4(bytes, 0, matrix));
            Assert.Equal(bits, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(60)));
        }
    }

    /// <summary>An incomplete source matrix is rejected before a valid destination is mutated.</summary>
    [Fact]
    public void ShortMatrixSourceDoesNotMutateDestination()
    {
        byte[] bytes = Enumerable.Repeat((byte)0xCD, 64).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => UboPacking.WriteMat4(bytes, 0, new float[15]));
        Assert.All(bytes, value => Assert.Equal((byte)0xCD, value));
    }
    #endregion

    #region Private
    /// <summary>Dispatches a packing family with nonzero values so each component is observable.</summary>
    private static bool Write(int writer, byte[] bytes, int offset) => writer switch
    {
        0 => UboPacking.WriteFloat(bytes, offset, -1.25f),
        1 => UboPacking.WriteInt32(bytes, offset, int.MinValue),
        2 => UboPacking.WriteUInt32(bytes, offset, uint.MaxValue),
        3 => UboPacking.WriteVec2(bytes, offset, 1, 2),
        4 => UboPacking.WriteVec3(bytes, offset, 1, 2, 3),
        5 => UboPacking.WriteVec4(bytes, offset, 1, 2, 3, 4),
        6 => UboPacking.WriteIVec4(bytes, offset, -1, int.MinValue, 3, int.MaxValue),
        7 => UboPacking.WriteUVec4(bytes, offset, 1, uint.MaxValue, 3, 2147483648),
        8 => UboPacking.WriteMat4(bytes, offset, Enumerable.Range(1, 16).Select(value => (float)value).ToArray()),
        _ => throw new ArgumentOutOfRangeException(nameof(writer))
    };
    #endregion
}
