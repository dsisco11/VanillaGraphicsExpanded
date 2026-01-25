using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests for <see cref="GpuBindlessTextureHandle"/>.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GpuBindlessTextureHandleIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    public GpuBindlessTextureHandleIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void CreateForTexture_MakeResidentAndDispose_TogglesResidency()
    {
        fixture.MakeCurrent();

        Assert.SkipWhen(!GpuBindlessTextureHandle.IsSupported, "GL_ARB_bindless_texture not supported by this context.");

        using var tex = Texture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using var handle = GpuBindlessTextureHandle.CreateForTexture(tex.TextureId, makeResident: true);

        Assert.NotEqual(0ul, handle.Handle);
        Assert.True(handle.IsResident);
        Assert.True(GL.Arb.IsTextureHandleResident(handle.Handle));

        handle.Dispose();

        Assert.False(GL.Arb.IsTextureHandleResident(handle.Handle));
    }
}

