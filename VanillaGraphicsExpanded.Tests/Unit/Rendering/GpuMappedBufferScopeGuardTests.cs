using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies mapped-buffer scope guards for disposed GPU buffers.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuMappedBufferScopeGuardTests
{
    private readonly HeadlessGLFixture fixture;

    #region Public API

    /// <summary>Creates the GPU fixture-backed mapped-buffer guard test instance.</summary>
    public GpuMappedBufferScopeGuardTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>A disposed vertex buffer cannot create a mapped scope.</summary>
    [Fact]
    public void TryMapScope_DisposedBuffer_ReturnsFalse()
    {
        // Arrange
        fixture.MakeCurrent();

        var vbo = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = "DisposedMappedVbo" }).RequireResource();
        vbo.Dispose();

        // Act
        bool result = vbo.TryMapScope<int>(0, 1, BufferMapAccess.Write, out GpuMappedBufferScope<int> scope);

        // Assert
        Assert.False(result);
        Assert.False(scope.IsMapped);
    }

    #endregion
}
