using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies VBO binding, detachment, and debug-name managed rules.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuVboManagedRulesTests
{
    private readonly HeadlessGLFixture _fixture;

    #region Public API

    /// <summary>Creates the GPU fixture-backed VBO managed-rules test instance.</summary>
    public GpuVboManagedRulesTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }


    /// <summary>Updates the VBO debug name through its managed setter.</summary>
[Fact]
    public void SetDebugName_ValidVbo_UpdatesDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vbo = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        const string newDebugName = "UpdatedVBO";

        // Act
        vbo.SetDebugName(newDebugName);

        // Assert
        Assert.Equal(newDebugName, vbo.DebugName);
    }


    /// <summary>Returns false when attempting to bind a disposed VBO.</summary>
[Fact]
    public void TryBind_DisposedVbo_ReturnsFalse()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vbo = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        vbo.Dispose();

        // Act
        bool result = vbo.TryBind();

        // Assert
        Assert.False(result);
    }

    /// <summary>Returns zero when detaching a previously disposed VBO.</summary>
    [Fact]
    public void Detach_DisposedVbo_ReturnsZero()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vbo = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        vbo.Dispose();

        // Act
        int result = (int)vbo.Detach();

        // Assert
        Assert.Equal(0, result);
    }

    /// <summary>Clears the VBO debug name when the setter receives null.</summary>
    [Fact]
    public void SetDebugName_NullName_AcceptsNull()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vbo = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = "Initial" }).RequireResource();

        // Act
        vbo.SetDebugName(null);

        // Assert
        Assert.Null(vbo.DebugName);
    }


    #endregion

}
