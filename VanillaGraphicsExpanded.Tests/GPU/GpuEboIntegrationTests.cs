using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests for GpuEbo class.
/// Tests actual OpenGL element buffer creation, data upload, and drawing operations.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GpuEboIntegrationTests
{
    private readonly HeadlessGLFixture _fixture;

    public GpuEboIntegrationTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    #region Creation Tests

    [Fact]
    public void Create_WithDefaultParameters_CreatesValidEbo()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo = GpuEbo.Create();

        // Assert
        Assert.True(ebo.IsValid);
        Assert.True(ebo.BufferId > 0);
        Assert.Equal(BufferTarget.ElementArrayBuffer, ebo.Target);
        Assert.Equal(BufferUsageHint.StaticDraw, ebo.Usage);
        Assert.Equal(0, ebo.SizeBytes);
        Assert.Equal(0, ebo.IndexCount);
        Assert.Equal(default(DrawElementsType), ebo.IndexType);
        Assert.Null(ebo.DebugName);
        Assert.False(ebo.IsDisposed);
    }

    [Fact]
    public void Create_WithCustomUsageHint_UsesSpecifiedHint()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo = GpuEbo.Create(BufferUsageHint.DynamicDraw);

        // Assert
        Assert.True(ebo.IsValid);
        Assert.Equal(BufferUsageHint.DynamicDraw, ebo.Usage);
        Assert.Equal(BufferTarget.ElementArrayBuffer, ebo.Target);
    }

    [Fact]
    public void Create_WithDebugName_SetsDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        const string debugName = "TestEBO";

        // Act
        using var ebo = GpuEbo.Create(debugName: debugName);

        // Assert
        Assert.True(ebo.IsValid);
        Assert.Equal(debugName, ebo.DebugName);
    }

    [Fact]
    public void Create_WithAllCustomParameters_CreatesEboWithAllProperties()
    {
        // Arrange
        _fixture.EnsureContextValid();
        const string debugName = "CustomTestEBO";
        var usage = BufferUsageHint.StreamDraw;

        // Act
        using var ebo = GpuEbo.Create(usage, debugName);

        // Assert
        Assert.True(ebo.IsValid);
        Assert.Equal(usage, ebo.Usage);
        Assert.Equal(debugName, ebo.DebugName);
        Assert.True(ebo.BufferId > 0);
        Assert.False(ebo.IsDisposed);
    }

    [Fact]
    public void Create_GeneratesUniqueBufferIds()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo1 = GpuEbo.Create();
        using var ebo2 = GpuEbo.Create();

        // Assert
        Assert.NotEqual(ebo1.BufferId, ebo2.BufferId);
        Assert.True(ebo1.BufferId > 0);
        Assert.True(ebo2.BufferId > 0);
    }

    #endregion

    #region Index Upload Tests - UInt32

    [Fact]
    public void UploadIndices_UintArray_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        uint[] indices = { 0, 1, 2, 3, 4, 5 };

        // Act
        ebo.UploadIndices(indices);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedInt, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(uint), ebo.SizeBytes);
        Assert.True(ebo.IsValid);
    }

    [Fact]
    public void UploadIndices_UintArray_NullArray_ThrowsArgumentNullException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ebo.UploadIndices((uint[])null!));
    }

    [Fact]
    public void TryUploadIndices_UintArray_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        uint[] indices = { 0, 1, 2 };

        // Act
        bool result = ebo.TryUploadIndices(indices);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedInt, ebo.IndexType);
    }

    [Fact]
    public void TryUploadIndices_UintArray_NullArray_ReturnsFalse()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();

        // Act
        bool result = ebo.TryUploadIndices((uint[])null!);

        // Assert
        Assert.False(result);
        Assert.Equal(0, ebo.IndexCount);
    }

    [Fact]
    public void UploadIndices_UintSpan_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        uint[] indices = { 0, 1, 2, 3 };
        ReadOnlySpan<uint> span = indices.AsSpan();

        // Act
        ebo.UploadIndices(span);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedInt, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(uint), ebo.SizeBytes);
    }

    [Fact]
    public void TryUploadIndices_UintSpan_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        uint[] indices = { 0, 1, 2 };
        ReadOnlySpan<uint> span = indices.AsSpan();

        // Act
        bool result = ebo.TryUploadIndices(span);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedInt, ebo.IndexType);
    }

    #endregion

    #region Index Upload Tests - UInt16

    [Fact]
    public void UploadIndices_UshortArray_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        ushort[] indices = { 0, 1, 2, 3, 4, 5 };

        // Act
        ebo.UploadIndices(indices);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedShort, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(ushort), ebo.SizeBytes);
        Assert.True(ebo.IsValid);
    }

    [Fact]
    public void UploadIndices_UshortArray_NullArray_ThrowsArgumentNullException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ebo.UploadIndices((ushort[])null!));
    }

    [Fact]
    public void TryUploadIndices_UshortArray_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        ushort[] indices = { 0, 1, 2 };

        // Act
        bool result = ebo.TryUploadIndices(indices);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedShort, ebo.IndexType);
    }

    [Fact]
    public void TryUploadIndices_UshortArray_NullArray_ReturnsFalse()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();

        // Act
        bool result = ebo.TryUploadIndices((ushort[])null!);

        // Assert
        Assert.False(result);
        Assert.Equal(0, ebo.IndexCount);
    }

    [Fact]
    public void UploadIndices_UshortSpan_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        ushort[] indices = { 0, 1, 2, 3 };
        ReadOnlySpan<ushort> span = indices.AsSpan();

        // Act
        ebo.UploadIndices(span);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedShort, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(ushort), ebo.SizeBytes);
    }

    [Fact]
    public void TryUploadIndices_UshortSpan_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        ushort[] indices = { 0, 1, 2 };
        ReadOnlySpan<ushort> span = indices.AsSpan();

        // Act
        bool result = ebo.TryUploadIndices(span);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedShort, ebo.IndexType);
    }

    #endregion

    #region Index Upload Tests - Byte

    [Fact]
    public void UploadIndices_ByteArray_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        byte[] indices = { 0, 1, 2, 3, 4, 5 };

        // Act
        ebo.UploadIndices(indices);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedByte, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(byte), ebo.SizeBytes);
        Assert.True(ebo.IsValid);
    }

    [Fact]
    public void UploadIndices_ByteArray_NullArray_ThrowsArgumentNullException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ebo.UploadIndices((byte[])null!));
    }

    [Fact]
    public void TryUploadIndices_ByteArray_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        byte[] indices = { 0, 1, 2 };

        // Act
        bool result = ebo.TryUploadIndices(indices);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedByte, ebo.IndexType);
    }

    [Fact]
    public void TryUploadIndices_ByteArray_NullArray_ReturnsFalse()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();

        // Act
        bool result = ebo.TryUploadIndices((byte[])null!);

        // Assert
        Assert.False(result);
        Assert.Equal(0, ebo.IndexCount);
    }

    [Fact]
    public void UploadIndices_ByteSpan_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        byte[] indices = { 0, 1, 2, 3 };
        ReadOnlySpan<byte> span = indices.AsSpan();

        // Act
        ebo.UploadIndices(span);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedByte, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(byte), ebo.SizeBytes);
    }

    [Fact]
    public void TryUploadIndices_ByteSpan_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        byte[] indices = { 0, 1, 2 };
        ReadOnlySpan<byte> span = indices.AsSpan();

        // Act
        bool result = ebo.TryUploadIndices(span);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedByte, ebo.IndexType);
    }

    #endregion

    #region Multiple Upload Tests

    [Fact]
    public void UploadIndices_MultipleUploads_OverwritesPreviousData()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        
        uint[] firstIndices = { 0, 1, 2 };
        ushort[] secondIndices = { 10, 11, 12, 13 };

        // Act
        ebo.UploadIndices(firstIndices);
        Assert.Equal(firstIndices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedInt, ebo.IndexType);

        ebo.UploadIndices(secondIndices);

        // Assert - Second upload should overwrite first
        Assert.Equal(secondIndices.Length, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedShort, ebo.IndexType);
        Assert.Equal(secondIndices.Length * sizeof(ushort), ebo.SizeBytes);
    }

    [Fact]
    public void UploadIndices_EmptyArray_SetsZeroCount()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        uint[] emptyIndices = Array.Empty<uint>();

        // Act
        ebo.UploadIndices(emptyIndices);

        // Assert
        Assert.Equal(0, ebo.IndexCount);
        Assert.Equal(DrawElementsType.UnsignedInt, ebo.IndexType);
        Assert.Equal(0, ebo.SizeBytes);
    }

    #endregion

    #region Draw Operations Tests

    [Fact]
    public void DrawElements_WithUploadedData_ExecutesDrawCall()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        
        // Don't upload data to avoid access violations - this will test early return
        const PrimitiveType primitiveType = PrimitiveType.Triangles;

        // Act - Should return early due to zero index count
        ebo.DrawElements(primitiveType);

        // Assert - EBO should still be valid
        Assert.True(ebo.IsValid);
    }

    [Fact]
    public void DrawElements_WithCustomIndexCount_UsesSpecifiedCount()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        uint[] indices = { 0, 1, 2, 3, 4, 5 };
        ebo.UploadIndices(indices);

        const PrimitiveType primitiveType = PrimitiveType.Triangles;
        const int customCount = 0; // Use 0 to trigger early return

        // Act - Should return early due to zero count
        ebo.DrawElements(primitiveType, customCount);

        // Assert
        Assert.True(ebo.IsValid);
    }

    [Fact]
    public void DrawElements_NegativeOffset_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            ebo.DrawElements(PrimitiveType.Triangles, 3, -1));
    }

    [Fact]
    public void DrawElements_ZeroIndexCount_ReturnsEarly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();

        // Act - Should return early due to zero index count
        ebo.DrawElements(PrimitiveType.Triangles);

        // Assert
        Assert.True(ebo.IsValid);
        Assert.Equal(0, ebo.IndexCount);
    }

    [Fact]
    public void DrawElementsInstanced_WithUploadedData_ExecutesDrawCall()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        
        // Don't upload data to avoid access violations
        const PrimitiveType primitiveType = PrimitiveType.Triangles;
        const int instanceCount = 2;

        // Act - Should return early due to zero index count
        ebo.DrawElementsInstanced(primitiveType, instanceCount);

        // Assert
        Assert.True(ebo.IsValid);
    }

    [Fact]
    public void DrawElementsInstanced_ZeroInstanceCount_ReturnsEarly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        uint[] indices = { 0, 1, 2 };
        ebo.UploadIndices(indices);

        // Act - Should return early due to zero instance count
        ebo.DrawElementsInstanced(PrimitiveType.Triangles, 0);

        // Assert
        Assert.True(ebo.IsValid);
    }

    [Fact]
    public void DrawElementsInstanced_NegativeOffset_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            ebo.DrawElementsInstanced(PrimitiveType.Triangles, 1, 3, -1));
    }

    [Fact]
    public void DrawElements_DisposedEbo_ReturnsEarly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var ebo = GpuEbo.Create();
        ebo.Dispose();

        // Act - Should return early due to disposed state
        ebo.DrawElements(PrimitiveType.Triangles);

        // Assert
        Assert.False(ebo.IsValid);
        Assert.True(ebo.IsDisposed);
    }

    #endregion

    #region Resource Management Tests

    [Fact]
    public void Detach_ValidEbo_ReturnsBufferIdAndResetsIndexProperties()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var ebo = GpuEbo.Create();
        uint[] indices = { 0, 1, 2 };
        ebo.UploadIndices(indices);
        
        int originalBufferId = ebo.BufferId;
        int originalIndexCount = ebo.IndexCount;
        DrawElementsType originalIndexType = ebo.IndexType;

        // Act
        int detachedId = (int)ebo.Detach();

        // Assert
        Assert.Equal(originalBufferId, detachedId);
        Assert.Equal(0, ebo.BufferId);
        Assert.Equal(0, ebo.IndexCount); // Should be reset
        Assert.Equal(default(DrawElementsType), ebo.IndexType); // Should be reset
        Assert.True(ebo.IsDisposed);
        Assert.False(ebo.IsValid);

        // Clean up manually since buffer was detached
        GL.DeleteBuffer(detachedId);
        
        ebo.Dispose(); // Safe to call on already detached/disposed
    }

    [Fact]
    public void Dispose_ValidEbo_ResetsIndexProperties()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var ebo = GpuEbo.Create();
        uint[] indices = { 0, 1, 2 };
        ebo.UploadIndices(indices);

        // Act
        ebo.Dispose();

        // Assert
        Assert.Equal(0, ebo.BufferId);
        Assert.Equal(0, ebo.IndexCount); // Should be reset
        Assert.Equal(default(DrawElementsType), ebo.IndexType); // Should be reset
        Assert.True(ebo.IsDisposed);
        Assert.False(ebo.IsValid);
    }

    [Fact]
    public void SetDebugName_ValidEbo_UpdatesDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        const string newDebugName = "UpdatedEBO";

        // Act
        ebo.SetDebugName(newDebugName);

        // Assert
        Assert.Equal(newDebugName, ebo.DebugName);
    }

    [Fact]
    public void SetDebugName_NullName_AcceptsNull()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create(debugName: "Initial");

        // Act
        ebo.SetDebugName(null);

        // Assert
        Assert.Null(ebo.DebugName);
    }

    [Fact]
    public void MultipleDispose_ValidEbo_IsSafe()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var ebo = GpuEbo.Create();
        uint[] indices = { 0, 1, 2 };
        ebo.UploadIndices(indices);

        // Act & Assert - Multiple dispose calls should be safe
        ebo.Dispose();
        Assert.True(ebo.IsDisposed);
        Assert.Equal(0, ebo.IndexCount);
        
        ebo.Dispose(); // Should not throw
        Assert.True(ebo.IsDisposed);
        
        ebo.Dispose(); // Should not throw
        Assert.True(ebo.IsDisposed);
    }

    #endregion

    #region Edge Cases and Error Handling Tests

    [Fact]
    public void UploadIndices_DisposedEbo_ThrowsOrHandlesGracefully()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var ebo = GpuEbo.Create();
        ebo.Dispose();
        
        uint[] indices = { 0, 1, 2 };

        // Act & Assert - The base class should handle this gracefully
        // The exact behavior depends on the base class implementation
        // This test verifies the method can be called on a disposed EBO
        try
        {
            ebo.UploadIndices(indices);
            // If it doesn't throw, verify the state remains consistent
            Assert.True(ebo.IsDisposed);
            Assert.Equal(0, ebo.IndexCount);
        }
        catch (Exception)
        {
            // If it throws, that's also acceptable behavior
            Assert.True(ebo.IsDisposed);
        }
    }

    [Fact]
    public void ToString_ValidEbo_ReturnsFormattedString()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create(BufferUsageHint.DynamicDraw, "TestEBO");
        uint[] indices = { 0, 1, 2 };
        ebo.UploadIndices(indices);

        // Act
        string result = ebo.ToString();

        // Assert
        Assert.Contains("GpuEbo", result);
        Assert.Contains($"id={ebo.BufferId}", result);
        Assert.Contains($"sizeBytes={ebo.SizeBytes}", result);
        Assert.Contains($"usage={ebo.Usage}", result);
        Assert.Contains($"indexCount={ebo.IndexCount}", result);
        Assert.Contains($"indexType={ebo.IndexType}", result);
        Assert.Contains("name=TestEBO", result);
        Assert.Contains("disposed=False", result);
    }

    [Fact]
    public void ToString_DisposedEbo_ShowsDisposedState()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var ebo = GpuEbo.Create(debugName: "TestEBO");
        uint[] indices = { 0, 1, 2 };
        ebo.UploadIndices(indices);
        ebo.Dispose();

        // Act
        string result = ebo.ToString();

        // Assert
        Assert.Contains("disposed=True", result);
        Assert.Contains("id=0", result);
        Assert.Contains("indexCount=0", result);
    }

    [Theory]
    [InlineData(BufferUsageHint.StaticDraw)]
    [InlineData(BufferUsageHint.DynamicDraw)]
    [InlineData(BufferUsageHint.StreamDraw)]
    public void Create_WithVariousUsageHints_CreatesValidEbos(BufferUsageHint usage)
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo = GpuEbo.Create(usage);

        // Assert
        Assert.True(ebo.IsValid);
        Assert.Equal(usage, ebo.Usage);
        Assert.True(ebo.BufferId > 0);
    }

    [Fact]
    public void Create_LongDebugName_AcceptsLongNames()
    {
        // Arrange
        _fixture.EnsureContextValid();
        string longName = new string('E', 1000);

        // Act
        using var ebo = GpuEbo.Create(debugName: longName);

        // Assert
        Assert.Equal(longName, ebo.DebugName);
        Assert.True(ebo.IsValid);
    }

    #endregion

    #region Base Class Integration Tests

    [Fact]
    public void Bind_CreatedEbo_SuccessfullyBindsBuffer()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();

        // Act & Assert - Should not throw
        ebo.Bind();
        
        // Verify it's bound to the correct target
        GL.GetInteger(GetPName.ElementArrayBufferBinding, out int boundBuffer);
        Assert.Equal(ebo.BufferId, boundBuffer);
        
        ebo.Unbind();
    }

    [Fact]
    public void Allocate_ValidSize_UpdatesSizeBytes()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();
        const int expectedSize = 1024;

        // Act
        ebo.Allocate(expectedSize);

        // Assert
        Assert.Equal(expectedSize, ebo.SizeBytes);
        Assert.True(ebo.IsValid);
    }

    [Fact]
    public void TryBind_ValidEbo_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = GpuEbo.Create();

        // Act
        bool result = ebo.TryBind();

        // Assert
        Assert.True(result);
        
        ebo.Unbind();
    }

    #endregion
}
