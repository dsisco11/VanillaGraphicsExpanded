using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>
/// Verifies EBO creation, data upload, native binding, and ownership transitions against OpenGL.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuEboIntegrationTests : IDisposable
{
    private readonly HeadlessGLFixture _fixture;

    #region Public API

    /// <summary>Creates the fixture-backed EBO integration test instance.</summary>
    public GpuEboIntegrationTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Checks that each EBO test leaves the native error state clean.</summary>
    public void Dispose() => Assert.Equal(ErrorCode.NoError, GL.GetError());

    #region Creation Tests

    /// <summary>Creates a valid EBO with the default parameters.</summary>
    [Fact]
    public void Create_WithDefaultParameters_CreatesValidEbo()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();

        // Assert
        Assert.True(ebo.IsValid);
        Assert.True(ebo.BufferId > 0);
        Assert.Equal(BufferTarget.ElementArrayBuffer, VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(ebo.Role));
        Assert.Equal(BufferUsage.StaticDraw, ebo.Usage);
        Assert.Equal(0, ebo.SizeBytes);
        Assert.Equal(0, ebo.IndexCount);
        Assert.Equal(default(IndexFormat), ebo.IndexType);
        Assert.Null(ebo.DebugName);
        Assert.False(ebo.IsDisposed);
    }

    /// <summary>Creates a buffer using the requested usage hint.</summary>
    [Fact]
    public void Create_WithCustomUsageHint_UsesSpecifiedHint()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();

        // Assert
        Assert.True(ebo.IsValid);
        AssertNativeUsage(ebo);
        ebo.Allocate(0);
        Assert.Equal(BufferUsage.DynamicDraw, ebo.Usage);
        Assert.Equal(BufferTarget.ElementArrayBuffer, VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(ebo.Role));
    }

    /// <summary>Creates a valid EBO with the requested debug name.</summary>
    [Fact]
    public void Create_WithDebugName_SetsDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        const string debugName = "TestEBO";

        // Act
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = debugName }).RequireResource();

        // Assert
        Assert.True(ebo.IsValid);
        Assert.Equal(debugName, ebo.DebugName);
    }

    /// <summary>Applies the supplied description values to the created EBO.</summary>
    [Fact]
    public void Create_WithAllCustomParameters_CreatesEboWithAllProperties()
    {
        // Arrange
        _fixture.EnsureContextValid();
        const string debugName = "CustomTestEBO";
        var usage = BufferUsageHint.StreamDraw;

        // Act
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Usage(usage), Label = debugName }).RequireResource();

        // Assert
        Assert.True(ebo.IsValid);
        AssertNativeUsage(ebo);
        ebo.Allocate(0);
        Assert.Equal(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Usage(usage), ebo.Usage);
        Assert.Equal(debugName, ebo.DebugName);
        Assert.True(ebo.BufferId > 0);
        Assert.False(ebo.IsDisposed);
    }

    /// <summary>Assigns distinct native buffer names to independently created buffers.</summary>
    [Fact]
    public void Create_GeneratesUniqueBufferIds()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo1 = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        using var ebo2 = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();

        // Assert
        Assert.NotEqual(ebo1.BufferId, ebo2.BufferId);
        Assert.True(ebo1.BufferId > 0);
        Assert.True(ebo2.BufferId > 0);
    }

    #endregion

    #region Index Upload Tests - UInt32

    /// <summary>Uploads UInt32 index data from the array and records its count, format, and byte size.</summary>
    [Fact]
    public void UploadIndices_UintArray_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        uint[] indices = { 0, 1, 2, 3, 4, 5 };

        // Act
        ebo.UploadIndices(indices);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedInt, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(uint), ebo.SizeBytes);
        Assert.True(ebo.IsValid);
    }


    /// <summary>Accepts valid UInt32 index data from the array and records its count and format.</summary>
[Fact]
    public void TryUploadIndices_UintArray_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        uint[] indices = { 0, 1, 2 };

        // Act
        bool result = ebo.TryUploadIndices(indices);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedInt, ebo.IndexType);
    }


    /// <summary>Uploads UInt32 index data from the span and records its count, format, and byte size.</summary>
[Fact]
    public void UploadIndices_UintSpan_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        uint[] indices = { 0, 1, 2, 3 };
        ReadOnlySpan<uint> span = indices.AsSpan();

        // Act
        ebo.UploadIndices(span);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedInt, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(uint), ebo.SizeBytes);
    }

    /// <summary>Accepts valid UInt32 index data from the span and records its count and format.</summary>
    [Fact]
    public void TryUploadIndices_UintSpan_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        uint[] indices = { 0, 1, 2 };
        ReadOnlySpan<uint> span = indices.AsSpan();

        // Act
        bool result = ebo.TryUploadIndices(span);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedInt, ebo.IndexType);
    }

    #endregion

    #region Index Upload Tests - UInt16

    /// <summary>Uploads UInt16 index data from the array and records its count, format, and byte size.</summary>
    [Fact]
    public void UploadIndices_UshortArray_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        ushort[] indices = { 0, 1, 2, 3, 4, 5 };

        // Act
        ebo.UploadIndices(indices);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedShort, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(ushort), ebo.SizeBytes);
        Assert.True(ebo.IsValid);
    }


    /// <summary>Accepts valid UInt16 index data from the array and records its count and format.</summary>
[Fact]
    public void TryUploadIndices_UshortArray_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        ushort[] indices = { 0, 1, 2 };

        // Act
        bool result = ebo.TryUploadIndices(indices);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedShort, ebo.IndexType);
    }


    /// <summary>Uploads UInt16 index data from the span and records its count, format, and byte size.</summary>
[Fact]
    public void UploadIndices_UshortSpan_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        ushort[] indices = { 0, 1, 2, 3 };
        ReadOnlySpan<ushort> span = indices.AsSpan();

        // Act
        ebo.UploadIndices(span);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedShort, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(ushort), ebo.SizeBytes);
    }

    /// <summary>Accepts valid UInt16 index data from the span and records its count and format.</summary>
    [Fact]
    public void TryUploadIndices_UshortSpan_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        ushort[] indices = { 0, 1, 2 };
        ReadOnlySpan<ushort> span = indices.AsSpan();

        // Act
        bool result = ebo.TryUploadIndices(span);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedShort, ebo.IndexType);
    }

    #endregion

    #region Index Upload Tests - Byte

    /// <summary>Uploads byte index data from the array and records its count, format, and byte size.</summary>
    [Fact]
    public void UploadIndices_ByteArray_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        byte[] indices = { 0, 1, 2, 3, 4, 5 };

        // Act
        ebo.UploadIndices(indices);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedByte, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(byte), ebo.SizeBytes);
        Assert.True(ebo.IsValid);
    }


    /// <summary>Accepts valid byte index data from the array and records its count and format.</summary>
[Fact]
    public void TryUploadIndices_ByteArray_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        byte[] indices = { 0, 1, 2 };

        // Act
        bool result = ebo.TryUploadIndices(indices);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedByte, ebo.IndexType);
    }


    /// <summary>Uploads byte index data from the span and records its count, format, and byte size.</summary>
[Fact]
    public void UploadIndices_ByteSpan_UploadsDataCorrectly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        byte[] indices = { 0, 1, 2, 3 };
        ReadOnlySpan<byte> span = indices.AsSpan();

        // Act
        ebo.UploadIndices(span);

        // Assert
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedByte, ebo.IndexType);
        Assert.Equal(indices.Length * sizeof(byte), ebo.SizeBytes);
    }

    /// <summary>Accepts valid byte index data from the span and records its count and format.</summary>
    [Fact]
    public void TryUploadIndices_ByteSpan_ValidData_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        byte[] indices = { 0, 1, 2 };
        ReadOnlySpan<byte> span = indices.AsSpan();

        // Act
        bool result = ebo.TryUploadIndices(span);

        // Assert
        Assert.True(result);
        Assert.Equal(indices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedByte, ebo.IndexType);
    }

    #endregion

    #region Multiple Upload Tests

    /// <summary>Replaces prior index data and updates the count, format, and size for the new upload.</summary>
    [Fact]
    public void UploadIndices_MultipleUploads_OverwritesPreviousData()
    {
        // Arrange
        _fixture.EnsureContextValid();
        
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        uint[] firstIndices = { 0, 1, 2 };
        ushort[] secondIndices = { 10, 11, 12, 13 };

        // Act
        ebo.UploadIndices(firstIndices);
        Assert.Equal(firstIndices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedInt, ebo.IndexType);

        ebo.UploadIndices(secondIndices);

        // Assert - Second upload should overwrite first
        Assert.Equal(secondIndices.Length, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedShort, ebo.IndexType);
        Assert.Equal(secondIndices.Length * sizeof(ushort), ebo.SizeBytes);
    }

    /// <summary>Sets the index count and byte size to zero when uploading an empty array.</summary>
    [Fact]
    public void UploadIndices_EmptyArray_SetsZeroCount()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        uint[] emptyIndices = Array.Empty<uint>();

        // Act
        ebo.UploadIndices(emptyIndices);

        // Assert
        Assert.Equal(0, ebo.IndexCount);
        Assert.Equal(IndexFormat.UnsignedInt, ebo.IndexType);
        Assert.Equal(0, ebo.SizeBytes);
    }

    #endregion

    #region Resource Management Tests

    /// <summary>Transfers the native buffer name and resets the EBO index metadata.</summary>
    [Fact]
    public void Detach_ValidEbo_ReturnsBufferIdAndResetsIndexProperties()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        uint[] indices = { 0, 1, 2 };
        ebo.UploadIndices(indices);
        
        int originalBufferId = ebo.BufferId;

        // Act
        int detachedId = 0;
        try
        {
            detachedId = (int)ebo.Detach();

            // Assert
            Assert.Equal(originalBufferId, detachedId);
            Assert.Equal(0, ebo.BufferId);
            Assert.Equal(0, ebo.IndexCount);
            Assert.Equal(default(IndexFormat), ebo.IndexType);
            Assert.True(ebo.IsDisposed);
            Assert.False(ebo.IsValid);
        }
        finally
        {
            // Detach transfers native ownership, so the test must delete the raw name even if an assertion fails.
            if (detachedId != 0) GL.DeleteBuffer(detachedId);
        }
    }

    /// <summary>Clears the native name and index metadata when the EBO is disposed.</summary>
    [Fact]
    public void Dispose_ValidEbo_ResetsIndexProperties()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        uint[] indices = { 0, 1, 2 };
        ebo.UploadIndices(indices);

        // Act
        ebo.Dispose();

        // Assert
        Assert.Equal(0, ebo.BufferId);
        Assert.Equal(0, ebo.IndexCount); // Should be reset
        Assert.Equal(default(IndexFormat), ebo.IndexType); // Should be reset
        Assert.True(ebo.IsDisposed);
        Assert.False(ebo.IsValid);
    }


    /// <summary>Allows repeated disposal of a EBO without changing its disposed state.</summary>
[Fact]
    public void MultipleDispose_ValidEbo_IsSafe()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
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


    /// <summary>Creates valid buffers for each supplied usage hint.</summary>
[Theory]
    [InlineData(BufferUsageHint.StaticDraw)]
    [InlineData(BufferUsageHint.DynamicDraw)]
    [InlineData(BufferUsageHint.StreamDraw)]
    public void Create_WithVariousUsageHints_CreatesValidEbos(BufferUsageHint usage)
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Usage(usage), Label = null }).RequireResource();

        // Assert
        Assert.True(ebo.IsValid);
        AssertNativeUsage(ebo);
        ebo.Allocate(0);
        Assert.Equal(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Usage(usage), ebo.Usage);
        Assert.True(ebo.BufferId > 0);
    }

    /// <summary>Preserves the full long debug name on the created EBO.</summary>
    [Fact]
    public void Create_LongDebugName_AcceptsLongNames()
    {
        // Arrange
        _fixture.EnsureContextValid();
        string longName = new string('E', 1000);

        // Act
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = longName }).RequireResource();

        // Assert
        Assert.Equal(longName, ebo.DebugName);
        Assert.True(ebo.IsValid);
    }

    #endregion

    #region Base Class Integration Tests

    /// <summary>Binds the created EBO to the element-array target.</summary>
    [Fact]
    public void Bind_CreatedEbo_SuccessfullyBindsBuffer()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();

        // Act & Assert - Should not throw
        ebo.Bind();
        
        // Verify it's bound to the correct target
        GL.GetInteger(GetPName.ElementArrayBufferBinding, out int boundBuffer);
        Assert.Equal(ebo.BufferId, boundBuffer);
        
        ebo.Unbind();
    }

    /// <summary>Records the requested EBO allocation size.</summary>
    [Fact]
    public void Allocate_ValidSize_UpdatesSizeBytes()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        const int expectedSize = 1024;

        // Act
        ebo.Allocate(expectedSize);

        // Assert
        Assert.Equal(expectedSize, ebo.SizeBytes);
        Assert.True(ebo.IsValid);
    }

    /// <summary>Returns true when a live EBO can be bound.</summary>
    [Fact]
    public void TryBind_ValidEbo_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var ebo = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        bool result = ebo.TryBind();

        // Assert
        Assert.True(result);
        
        ebo.Unbind();
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Checks realized native storage before requested allocation policy has been applied.</summary>
    private static void AssertNativeUsage(GpuBufferObject buffer)
    {
        // Array-buffer binding can query any buffer without altering a VAO's element binding.
        using var binding = OpenGlStateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, buffer.BufferId);
        GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferUsage, out int actualUsage);
        Assert.Equal(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Usage((BufferUsageHint)actualUsage), buffer.Usage);
    }
    #endregion
}
