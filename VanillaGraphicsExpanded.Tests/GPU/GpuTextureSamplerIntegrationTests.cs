using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests verifying that <see cref="GpuTexture"/> uses texture-object parameters (TexParameter)
/// and unbinds any sampler object that would override them.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GpuTextureSamplerIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    public GpuTextureSamplerIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void Texture2D_Bind_UnbindsSampler_AndHasExpectedTextureObjectParameters()
    {
        fixture.MakeCurrent();

        using var tex = Texture2D.Create(4, 4, PixelInternalFormat.Rgba8, filter: TextureFilterMode.Linear, debugName: "Test.Tex");

        Assert.True(tex.IsValid);

        const int unit = 3;
        tex.Bind(unit);

        Assert.Equal(0, GlStateCache.Current.GetBoundSampler(unit));

        using (GlStateCache.Current.BindTextureScope(tex.TextureTarget, unit: 0, tex.TextureId))
        {
            GL.GetTexParameter(tex.TextureTarget, GetTextureParameter.TextureMinFilter, out int minFilter);
            GL.GetTexParameter(tex.TextureTarget, GetTextureParameter.TextureMagFilter, out int magFilter);
            GL.GetTexParameter(tex.TextureTarget, GetTextureParameter.TextureWrapS, out int wrapS);
            GL.GetTexParameter(tex.TextureTarget, GetTextureParameter.TextureWrapT, out int wrapT);

            Assert.Equal((int)TextureMinFilter.Linear, minFilter);
            Assert.Equal((int)TextureMagFilter.Linear, magFilter);
            Assert.Equal((int)TextureWrapMode.ClampToEdge, wrapS);
            Assert.Equal((int)TextureWrapMode.ClampToEdge, wrapT);
        }

        tex.Unbind(unit);
        Assert.Equal(0, GlStateCache.Current.GetBoundSampler(unit));
    }

    [Fact]
    public void BindScope_RestoresPreviousTextureAndSampler()
    {
        fixture.MakeCurrent();

        using var t1 = Texture2D.Create(4, 4, PixelInternalFormat.Rgba8, filter: TextureFilterMode.Nearest, debugName: "Test.T1");
        using var t2 = Texture2D.Create(4, 4, PixelInternalFormat.Rgba8, filter: TextureFilterMode.Linear, debugName: "Test.T2");
        using var s1 = GpuSampler.Create("Test.S1");

        const int unit = 2;

        // Establish a known texture+sampler binding without going through GpuTexture.Bind (which unbinds samplers).
        GlStateCache.Current.BindTexture(t1.TextureTarget, unit, t1.TextureId);
        GlStateCache.Current.BindSampler(unit, s1);

        int prevTexId = GlStateCache.Current.GetBoundTexture(t1.TextureTarget, unit);
        int prevSamplerId = GlStateCache.Current.GetBoundSampler(unit);
        Assert.Equal(t1.TextureId, prevTexId);
        Assert.Equal(s1.SamplerId, prevSamplerId);

        using (t2.BindScope(unit))
        {
            Assert.Equal(t2.TextureId, GlStateCache.Current.GetBoundTexture(t2.TextureTarget, unit));
            Assert.Equal(0, GlStateCache.Current.GetBoundSampler(unit));
        }

        Assert.Equal(t1.TextureId, GlStateCache.Current.GetBoundTexture(t1.TextureTarget, unit));
        Assert.Equal(s1.SamplerId, GlStateCache.Current.GetBoundSampler(unit));
    }
}
