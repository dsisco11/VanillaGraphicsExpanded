using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks typed packing, adjacent-member preservation and dirty tracking without a GL context.</summary>
public sealed class CpuUniformBufferTests
{
    #region Packing contracts
    /// <summary>A vec3 write must preserve the scalar occupying the fourth component of its slot.</summary>
    [Fact]
    public void VectorWritePreservesAdjacentScalar()
    {
        using var buffer = new TestBuffer();
        Assert.False(buffer.IsDirty);
        buffer.Scalar(12, .75f);
        buffer.Vector(0, new Vector3(1, -2, 3));
        Assert.Equal(new float[] { 1, -2, 3, .75f }, MemoryMarshal.Cast<byte, float>(buffer.Bytes[..16]).ToArray());
        Assert.True(buffer.IsDirty);
    }

    /// <summary>Integer writes preserve signed values and all unsigned bits rather than converting to floats.</summary>
    [Fact]
    public void IntegersRetainTheirBitPatterns()
    {
        using var buffer = new TestBuffer();
        buffer.Signed(0, int.MinValue);
        buffer.Unsigned(4, uint.MaxValue);
        buffer.SignedVector(16);
        buffer.UnsignedVector(32);
        Assert.Equal(int.MinValue, MemoryMarshal.Read<int>(buffer.Bytes));
        Assert.Equal(uint.MaxValue, MemoryMarshal.Read<uint>(buffer.Bytes[4..]));
        Assert.Equal(new int[] { -1, 0, 1, int.MaxValue }, MemoryMarshal.Cast<byte, int>(buffer.Bytes.Slice(16, 16)).ToArray());
        Assert.Equal(new uint[] { 0, 1, uint.MaxValue, 2147483648 }, MemoryMarshal.Cast<byte, uint>(buffer.Bytes.Slice(32, 16)).ToArray());
        Assert.True(buffer.IsDirty);
    }

    /// <summary>Numerics translation lands in the GLSL fourth column without an extra transpose.</summary>
    [Fact]
    public void NumericsMatrixPreservesTransformConvention()
    {
        using var buffer = new TestBuffer();
        buffer.Matrix(Matrix4x4.CreateTranslation(2, 3, 4));
        Assert.Equal(new float[] { 2, 3, 4, 1 }, MemoryMarshal.Cast<byte, float>(buffer.Bytes.Slice(48, 16)).ToArray());
        Assert.True(buffer.IsDirty);
    }

    /// <summary>Rejected writes leave both bytes and dirty status untouched.</summary>
    [Fact]
    public void InvalidVectorWriteDoesNotPartiallyMutateBuffer()
    {
        using var buffer = new TestBuffer();
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Vector(60, Vector3.One));
        Assert.False(buffer.IsDirty);
        Assert.All(buffer.Bytes.ToArray(), value => Assert.Equal((byte)0, value));
    }
    #endregion

    /// <summary>Exposes protected operations for tests without changing the production public API.</summary>
    private sealed class TestBuffer : CpuUniformBuffer
    {
        /// <summary>Allocates one matrix-sized block.</summary>
        internal TestBuffer() : base(64) { }
        /// <summary>Writes a floating scalar.</summary>
        internal void Scalar(int offset, float value) => WriteFloat(offset, value);
        /// <summary>Writes three floats.</summary>
        internal void Vector(int offset, Vector3 value) => WriteVector3(offset, value);
        /// <summary>Writes a signed scalar.</summary>
        internal void Signed(int offset, int value) => WriteInt32(offset, value);
        /// <summary>Writes an unsigned scalar.</summary>
        internal void Unsigned(int offset, uint value) => WriteUInt32(offset, value);
        /// <summary>Writes signed boundary values.</summary>
        internal void SignedVector(int offset) => WriteIntVector4(offset, -1, 0, 1, int.MaxValue);
        /// <summary>Writes unsigned boundary values.</summary>
        internal void UnsignedVector(int offset) => WriteUIntVector4(offset, 0, 1, uint.MaxValue, 2147483648);
        /// <summary>Writes a Numerics transform.</summary>
        internal void Matrix(Matrix4x4 value) => WriteMatrix4(0, value);
    }
}
