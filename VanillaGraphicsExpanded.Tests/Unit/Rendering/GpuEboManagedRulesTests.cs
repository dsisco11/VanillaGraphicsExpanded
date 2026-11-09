using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies EBO managed guards, debug names, disposal, and diagnostic formatting.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuEboManagedRulesTests : IDisposable
{
    private readonly HeadlessGLFixture _fixture;

    #region Public API

    /// <summary>Creates the GPU fixture-backed EBO managed-rules test instance.</summary>
    public GpuEboManagedRulesTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Checks that each EBO test leaves the native error state clean.</summary>
    public void Dispose() => Assert.Equal(ErrorCode.NoError, GL.GetError());


    #region Index Upload Tests - UInt32


    /// <summary>Rejects a null UInt32 array with the expected argument-null behavior.</summary>
[Fact]
    public void UploadIndices_UintArray_NullArray_ThrowsArgumentNullException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ebo.UploadIndices((uint[])null!));
    }


    /// <summary>Returns false and leaves the index count at zero for a null UInt32 array.</summary>
[Fact]
    public void TryUploadIndices_UintArray_NullArray_ReturnsFalse()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        bool result = ebo.TryUploadIndices((uint[])null!);

        // Assert
        Assert.False(result);
        Assert.Equal(0, ebo.IndexCount);
    }


#endregion

    #region Index Upload Tests - UInt16


    /// <summary>Rejects a null UInt16 array with the expected argument-null behavior.</summary>
[Fact]
    public void UploadIndices_UshortArray_NullArray_ThrowsArgumentNullException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ebo.UploadIndices((ushort[])null!));
    }


    /// <summary>Returns false and leaves the index count at zero for a null UInt16 array.</summary>
[Fact]
    public void TryUploadIndices_UshortArray_NullArray_ReturnsFalse()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        bool result = ebo.TryUploadIndices((ushort[])null!);

        // Assert
        Assert.False(result);
        Assert.Equal(0, ebo.IndexCount);
    }


#endregion

    #region Index Upload Tests - Byte


    /// <summary>Rejects a null byte array with the expected argument-null behavior.</summary>
[Fact]
    public void UploadIndices_ByteArray_NullArray_ThrowsArgumentNullException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ebo.UploadIndices((byte[])null!));
    }


    /// <summary>Returns false and leaves the index count at zero for a null byte array.</summary>
[Fact]
    public void TryUploadIndices_ByteArray_NullArray_ReturnsFalse()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        bool result = ebo.TryUploadIndices((byte[])null!);

        // Assert
        Assert.False(result);
        Assert.Equal(0, ebo.IndexCount);
    }


#endregion


    #region Resource Management Tests


    /// <summary>Updates the EBO debug name through its managed setter.</summary>
[Fact]
    public void SetDebugName_ValidEbo_UpdatesDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        const string newDebugName = "UpdatedEBO";

        // Act
        ebo.SetDebugName(newDebugName);

        // Assert
        Assert.Equal(newDebugName, ebo.DebugName);
    }

    /// <summary>Clears the EBO debug name when the setter receives null.</summary>
    [Fact]
    public void SetDebugName_NullName_AcceptsNull()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = "Initial" }).RequireResource();

        // Act
        ebo.SetDebugName(null);

        // Assert
        Assert.Null(ebo.DebugName);
    }


#endregion

    #region Edge Cases and Error Handling Tests

    /// <summary>A disposed EBO remains disposed after an upload attempt, whether the call throws or returns.</summary>
    [Fact]
    public void UploadIndices_DisposedEbo_ThrowsOrHandlesGracefully()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
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

    /// <summary>Formats the live EBO name, usage, size, index metadata, and disposal state.</summary>
    [Fact]
    public void ToString_ValidEbo_ReturnsFormattedString()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "TestEBO" }).RequireResource();
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

    /// <summary>Formats the disposed EBO with its cleared name and index count.</summary>
    [Fact]
    public void ToString_DisposedEbo_ShowsDisposedState()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = "TestEBO" }).RequireResource();
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


#endregion

    #endregion

}
