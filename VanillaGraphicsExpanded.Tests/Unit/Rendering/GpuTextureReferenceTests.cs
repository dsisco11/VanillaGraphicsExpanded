using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Exercises consumer references across metadata changes and owner retirement.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuTextureReferenceTests(HeadlessGLFixture fixture)
{
    #region Public API


/// <summary>Null conversions preserve empty semantics without accessing native state.</summary>
    [Fact]
    public void NullWrappersProduceEmptyReferences()
    {
        // Arrange: Convert absent texture and view wrappers through their public reference conversions.
        GpuTexture? texture = null;
        GpuTextureView? view = null;
        GpuTextureRef first = texture;
        GpuTextureRef second = view;
        // Act and assert: Both references stay empty and cannot expose metadata or a fabricated native identity.
        Assert.True(first.IsEmpty);
        Assert.Equal(first, second);
        Assert.False(first.TryGetMetadata(out _));
        Assert.Throws<InvalidOperationException>(() => first.TextureId);
    }


    #endregion
}
