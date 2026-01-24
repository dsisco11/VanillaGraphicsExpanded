using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests for GpuVbo class.
/// Tests actual OpenGL buffer creation, binding, and resource management.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GpuVboIntegrationTests
{
    private readonly HeadlessGLFixture _fixture;

    public GpuVboIntegrationTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Create_WithDefaultParameters_CreatesValidVbo()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vbo = GpuVbo.Create();

        // Assert
        Assert.True(vbo.IsValid);
        Assert.True(vbo.BufferId > 0);
        Assert.Equal(BufferTarget.ArrayBuffer, vbo.Target);
        Assert.Equal(BufferUsageHint.StaticDraw, vbo.Usage);
        Assert.Equal(0, vbo.SizeBytes);
        Assert.Null(vbo.DebugName);
        Assert.False(vbo.IsDisposed);
    }

    [Fact]
    public void Create_WithCustomTarget_UsesSpecifiedTarget()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vbo = GpuVbo.Create(BufferTarget.ElementArrayBuffer);

        // Assert
        Assert.True(vbo.IsValid);
        Assert.Equal(BufferTarget.ElementArrayBuffer, vbo.Target);
        Assert.Equal(BufferUsageHint.StaticDraw, vbo.Usage);
    }

    [Fact]
    public void Create_WithCustomUsageHint_UsesSpecifiedHint()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vbo = GpuVbo.Create(usage: BufferUsageHint.DynamicDraw);

        // Assert
        Assert.True(vbo.IsValid);
        Assert.Equal(BufferTarget.ArrayBuffer, vbo.Target);
        Assert.Equal(BufferUsageHint.DynamicDraw, vbo.Usage);
    }

    [Fact]
    public void Create_WithDebugName_SetsDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        const string debugName = "TestVBO";

        // Act
        using var vbo = GpuVbo.Create(debugName: debugName);

        // Assert
        Assert.True(vbo.IsValid);
        Assert.Equal(debugName, vbo.DebugName);
    }

    [Fact]
    public void Create_WithAllCustomParameters_CreatesVboWithAllProperties()
    {
        // Arrange
        _fixture.EnsureContextValid();
        const string debugName = "CustomTestVBO";
        var target = BufferTarget.ElementArrayBuffer;
        var usage = BufferUsageHint.DynamicDraw;

        // Act
        using var vbo = GpuVbo.Create(target, usage, debugName);

        // Assert
        Assert.True(vbo.IsValid);
        Assert.Equal(target, vbo.Target);
        Assert.Equal(usage, vbo.Usage);
        Assert.Equal(debugName, vbo.DebugName);
        Assert.True(vbo.BufferId > 0);
        Assert.False(vbo.IsDisposed);
    }

    [Fact]
    public void Create_GeneratesUniqueBufferIds()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vbo1 = GpuVbo.Create();
        using var vbo2 = GpuVbo.Create();

        // Assert
        Assert.NotEqual(vbo1.BufferId, vbo2.BufferId);
        Assert.True(vbo1.BufferId > 0);
        Assert.True(vbo2.BufferId > 0);
    }

    [Fact]
    public void Bind_CreatedVbo_SuccessfullyBindsBuffer()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vbo = GpuVbo.Create();

        // Act & Assert - Should not throw
        vbo.Bind();
        
        // Verify it's actually bound to the correct target
        GL.GetInteger(GetPName.ArrayBufferBinding, out int boundBuffer);
        Assert.Equal(vbo.BufferId, boundBuffer);
        
        vbo.Unbind();
    }

    [Fact]
    public void Allocate_ValidSize_UpdatesSizeBytes()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vbo = GpuVbo.Create();
        const int expectedSize = 1024;

        // Act
        vbo.Allocate(expectedSize);

        // Assert
        Assert.Equal(expectedSize, vbo.SizeBytes);
        Assert.True(vbo.IsValid);
    }

    [Fact]
    public void TryBind_ValidVbo_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vbo = GpuVbo.Create();

        // Act
        bool result = vbo.TryBind();

        // Assert
        Assert.True(result);
        
        vbo.Unbind();
    }

    [Fact]
    public void BindScope_ValidVbo_RestoresPreviousBinding()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vbo1 = GpuVbo.Create();
        using var vbo2 = GpuVbo.Create();

        // Bind first VBO
        vbo1.Bind();
        GL.GetInteger(GetPName.ArrayBufferBinding, out int initialBinding);
        Assert.Equal(vbo1.BufferId, initialBinding);

        // Act - Use binding scope with second VBO
        using (var scope = vbo2.BindScope())
        {
            GL.GetInteger(GetPName.ArrayBufferBinding, out int scopedBinding);
            Assert.Equal(vbo2.BufferId, scopedBinding);
        }

        // Assert - Should restore original binding
        GL.GetInteger(GetPName.ArrayBufferBinding, out int restoredBinding);
        Assert.Equal(vbo1.BufferId, restoredBinding);
        
        vbo1.Unbind();
    }

    [Fact]
    public void SetDebugName_ValidVbo_UpdatesDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vbo = GpuVbo.Create();
        const string newDebugName = "UpdatedVBO";

        // Act
        vbo.SetDebugName(newDebugName);

        // Assert
        Assert.Equal(newDebugName, vbo.DebugName);
    }

    [Fact]
    public void Detach_ValidVbo_ReturnsBufferIdAndInvalidatesVbo()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vbo = GpuVbo.Create();
        int originalBufferId = vbo.BufferId;

        // Act
        int detachedId = (int)vbo.Detach();

        // Assert
        Assert.Equal(originalBufferId, detachedId);
        Assert.Equal(0, vbo.BufferId);
        Assert.True(vbo.IsDisposed);
        Assert.False(vbo.IsValid);
        Assert.Equal(0, vbo.SizeBytes);

        // Clean up the detached buffer manually since the VBO no longer owns it
        GL.DeleteBuffer(detachedId);
        
        vbo.Dispose(); // Safe to call on already detached/disposed
    }

    [Fact]
    public void Dispose_ValidVbo_DeletesBufferAndInvalidatesVbo()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vbo = GpuVbo.Create();
        int originalBufferId = vbo.BufferId;

        // Act
        vbo.Dispose();

        // Assert
        Assert.Equal(0, vbo.BufferId);
        Assert.True(vbo.IsDisposed);
        Assert.False(vbo.IsValid);
        Assert.Equal(0, vbo.SizeBytes);

        // Verify buffer was actually deleted by OpenGL
        // Note: We can't directly test if GL.DeleteBuffer was called, but we can verify
        // the VBO is in the expected disposed state
    }

    [Fact]
    public void MultipleVbos_WithDifferentTargets_WorkIndependently()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var arrayVbo = GpuVbo.Create(BufferTarget.ArrayBuffer, debugName: "ArrayVBO");
        using var elementVbo = GpuVbo.Create(BufferTarget.ElementArrayBuffer, debugName: "ElementVBO");

        // Assert
        Assert.True(arrayVbo.IsValid);
        Assert.True(elementVbo.IsValid);
        Assert.NotEqual(arrayVbo.BufferId, elementVbo.BufferId);
        Assert.Equal(BufferTarget.ArrayBuffer, arrayVbo.Target);
        Assert.Equal(BufferTarget.ElementArrayBuffer, elementVbo.Target);
        Assert.Equal("ArrayVBO", arrayVbo.DebugName);
        Assert.Equal("ElementVBO", elementVbo.DebugName);
    }

    [Fact]
    public void EnsureCapacity_ValidVbo_GrowsBuffer()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vbo = GpuVbo.Create();
        const int initialSize = 512;
        const int targetSize = 2048;

        // Act
        vbo.Allocate(initialSize);
        Assert.Equal(initialSize, vbo.SizeBytes);

        vbo.EnsureCapacity(targetSize);

        // Assert
        Assert.True(vbo.SizeBytes >= targetSize);
        Assert.True(vbo.IsValid);
    }

    [Theory]
    [InlineData(BufferTarget.ArrayBuffer)]
    [InlineData(BufferTarget.ElementArrayBuffer)]
    [InlineData(BufferTarget.UniformBuffer)]
    public void Create_WithVariousBufferTargets_CreatesValidVbos(BufferTarget target)
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vbo = GpuVbo.Create(target);

        // Assert
        Assert.True(vbo.IsValid);
        Assert.Equal(target, vbo.Target);
        Assert.True(vbo.BufferId > 0);
    }

    [Theory]
    [InlineData(BufferUsageHint.StaticDraw)]
    [InlineData(BufferUsageHint.DynamicDraw)]
    [InlineData(BufferUsageHint.StreamDraw)]
    public void Create_WithVariousUsageHints_CreatesValidVbos(BufferUsageHint usage)
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vbo = GpuVbo.Create(usage: usage);

        // Assert
        Assert.True(vbo.IsValid);
        Assert.Equal(usage, vbo.Usage);
        Assert.True(vbo.BufferId > 0);
    }

    [Fact]
    public void TryAllocate_ValidVbo_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vbo = GpuVbo.Create();
        const int size = 1024;

        // Act
        bool result = vbo.TryAllocate(size);

        // Assert
        Assert.True(result);
        Assert.Equal(size, vbo.SizeBytes);
    }

    [Fact]
    public void TryBind_DisposedVbo_ReturnsFalse()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vbo = GpuVbo.Create();
        vbo.Dispose();

        // Act
        bool result = vbo.TryBind();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Detach_DisposedVbo_ReturnsZero()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vbo = GpuVbo.Create();
        vbo.Dispose();

        // Act
        int result = (int)vbo.Detach();

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void SetDebugName_NullName_AcceptsNull()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vbo = GpuVbo.Create(debugName: "Initial");

        // Act
        vbo.SetDebugName(null);

        // Assert
        Assert.Null(vbo.DebugName);
    }

    [Fact]
    public void Create_EmptyDebugName_AcceptsEmptyString()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vbo = GpuVbo.Create(debugName: "");

        // Assert
        Assert.Equal("", vbo.DebugName);
    }

    [Fact]
    public void ReleaseHandle_ValidVbo_ReturnsSameAsDetach()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vbo1 = GpuVbo.Create();
        var vbo2 = GpuVbo.Create();
        int bufferId1 = vbo1.BufferId;
        int bufferId2 = vbo2.BufferId;

        // Act
        int detachResult = (int)vbo1.Detach();
        int releaseResult = (int)vbo2.ReleaseHandle();

        // Assert
        Assert.Equal(bufferId1, detachResult);
        Assert.Equal(bufferId2, releaseResult);
        Assert.True(vbo1.IsDisposed);
        Assert.True(vbo2.IsDisposed);

        // Clean up manually since buffers were detached
        GL.DeleteBuffer(detachResult);
        GL.DeleteBuffer(releaseResult);
        
        vbo1.Dispose();
        vbo2.Dispose();
    }

    [Fact]
    public void MultipleDispose_ValidVbo_IsSafe()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vbo = GpuVbo.Create();

        // Act & Assert - Multiple dispose calls should be safe
        vbo.Dispose();
        Assert.True(vbo.IsDisposed);
        
        vbo.Dispose(); // Second dispose should not throw
        Assert.True(vbo.IsDisposed);
        
        vbo.Dispose(); // Third dispose should not throw
        Assert.True(vbo.IsDisposed);
    }

    [Fact]
    public void Create_LongDebugName_AcceptsLongNames()
    {
        // Arrange
        _fixture.EnsureContextValid();
        string longName = new string('A', 1000); // Very long debug name

        // Act
        using var vbo = GpuVbo.Create(debugName: longName);

        // Assert
        Assert.Equal(longName, vbo.DebugName);
        Assert.True(vbo.IsValid);
    }
}
