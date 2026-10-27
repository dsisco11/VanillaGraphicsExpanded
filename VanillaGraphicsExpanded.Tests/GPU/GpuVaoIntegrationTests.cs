using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests for GpuVao class.
/// Tests the backend-owned vertex array lifetime; rendering behavior is covered through RHI geometry submission.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GpuVaoIntegrationTests
{
    private readonly HeadlessGLFixture _fixture;

    public GpuVaoIntegrationTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    #region Creation Tests

    [Fact]
    public void Create_WithDefaultParameters_CreatesValidVao()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vao = GpuVao.Create();

        // Assert
        Assert.True(vao.IsValid);
        Assert.True(vao.VertexArrayId > 0);
        Assert.Null(vao.DebugName);
        Assert.False(vao.IsDisposed);
    }

    [Fact]
    public void Create_WithDebugName_SetsDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        const string debugName = "TestVAO";

        // Act
        using var vao = GpuVao.Create(debugName);

        // Assert
        Assert.True(vao.IsValid);
        Assert.Equal(debugName, vao.DebugName);
        Assert.True(vao.VertexArrayId > 0);
    }

    [Fact]
    public void Create_GeneratesUniqueVertexArrayIds()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vao1 = GpuVao.Create();
        using var vao2 = GpuVao.Create();

        // Assert
        Assert.NotEqual(vao1.VertexArrayId, vao2.VertexArrayId);
        Assert.True(vao1.VertexArrayId > 0);
        Assert.True(vao2.VertexArrayId > 0);
    }

    [Fact]
    public void Create_EmptyDebugName_AcceptsEmptyString()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vao = GpuVao.Create("");

        // Assert
        Assert.Equal("", vao.DebugName);
        Assert.True(vao.IsValid);
    }

    #endregion

    #region Resource Management Tests

    [Fact]
    public void Detach_ValidVao_ReturnsVertexArrayIdAndInvalidatesVao()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create();
        int originalId = vao.VertexArrayId;

        // Act
        int detachedId = (int)vao.Detach();

        // Assert
        Assert.Equal(originalId, detachedId);
        Assert.Equal(0, vao.VertexArrayId);
        Assert.True(vao.IsDisposed);
        Assert.False(vao.IsValid);

        // Clean up the detached VAO manually
        GL.DeleteVertexArray(detachedId);
        
        vao.Dispose(); // Safe to call on already detached/disposed
    }

    [Fact]
    public void ReleaseHandle_ValidVao_ReturnsSameAsDetach()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao1 = GpuVao.Create();
        var vao2 = GpuVao.Create();
        int id1 = vao1.VertexArrayId;
        int id2 = vao2.VertexArrayId;

        // Act
        int detachResult = (int)vao1.Detach();
        int releaseResult = (int)vao2.ReleaseHandle();

        // Assert
        Assert.Equal(id1, detachResult);
        Assert.Equal(id2, releaseResult);
        Assert.True(vao1.IsDisposed);
        Assert.True(vao2.IsDisposed);

        // Clean up manually
        GL.DeleteVertexArray(detachResult);
        GL.DeleteVertexArray(releaseResult);
        
        vao1.Dispose();
        vao2.Dispose();
    }

    [Fact]
    public void Detach_DisposedVao_ReturnsZero()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create();
        vao.Dispose();

        // Act
        int result = (int)vao.Detach();

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void SetDebugName_ValidVao_UpdatesDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        const string newDebugName = "UpdatedVAO";

        // Act
        vao.SetDebugName(newDebugName);

        // Assert
        Assert.Equal(newDebugName, vao.DebugName);
    }

    [Fact]
    public void SetDebugName_NullName_AcceptsNull()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create("Initial");

        // Act
        vao.SetDebugName(null);

        // Assert
        Assert.Null(vao.DebugName);
    }

    [Fact]
    public void Dispose_ValidVao_DeletesVertexArrayAndInvalidatesVao()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create();

        // Act
        vao.Dispose();

        // Assert
        Assert.Equal(0, vao.VertexArrayId);
        Assert.True(vao.IsDisposed);
        Assert.False(vao.IsValid);
    }

    [Fact]
    public void MultipleDispose_ValidVao_IsSafe()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create();

        // Act & Assert - Multiple dispose calls should be safe
        vao.Dispose();
        Assert.True(vao.IsDisposed);
        
        vao.Dispose(); // Should not throw
        Assert.True(vao.IsDisposed);
        
        vao.Dispose(); // Should not throw
        Assert.True(vao.IsDisposed);
    }

    #endregion

    #region Edge Cases and Error Handling Tests

    [Fact]
    public void ToString_ValidVao_ReturnsFormattedString()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create("TestVAO");

        // Act
        string result = vao.ToString();

        // Assert
        Assert.Contains("GpuVao", result);
        Assert.Contains($"id={vao.VertexArrayId}", result);
        Assert.Contains("name=TestVAO", result);
        Assert.Contains("disposed=False", result);
    }

    [Fact]
    public void ToString_DisposedVao_ShowsDisposedState()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create("TestVAO");
        vao.Dispose();

        // Act
        string result = vao.ToString();

        // Assert
        Assert.Contains("disposed=True", result);
        Assert.Contains("id=0", result);
    }

    [Fact]
    public void Create_LongDebugName_AcceptsLongNames()
    {
        // Arrange
        _fixture.EnsureContextValid();
        string longName = new string('A', 1000);

        // Act
        using var vao = GpuVao.Create(longName);

        // Assert
        Assert.Equal(longName, vao.DebugName);
        Assert.True(vao.IsValid);
    }

    #endregion
}
