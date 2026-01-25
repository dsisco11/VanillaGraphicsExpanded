using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests verifying that <see cref="GpuTexture"/> binds a <see cref="GpuSampler"/> for filter/wrap state.
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
    public void Texture2D_Bind_BindsSamplerToUnit_WithExpectedParameters()
    {
        fixture.MakeCurrent();

        using var tex = Texture2D.Create(4, 4, PixelInternalFormat.Rgba8, filter: TextureFilterMode.Linear, debugName: "Test.Tex");

        Assert.True(tex.IsValid);
        Assert.True(tex.SamplerId > 0);

        const int unit = 3;
        tex.Bind(unit);

        Assert.Equal(tex.SamplerId, GlStateCache.Current.GetBoundSampler(unit));

        GL.GetSamplerParameterI(tex.SamplerId, SamplerParameterI.TextureMinFilter, out int minFilter);
        GL.GetSamplerParameterI(tex.SamplerId, SamplerParameterI.TextureMagFilter, out int magFilter);
        GL.GetSamplerParameterI(tex.SamplerId, SamplerParameterI.TextureWrapS, out int wrapS);
        GL.GetSamplerParameterI(tex.SamplerId, SamplerParameterI.TextureWrapT, out int wrapT);

        Assert.Equal((int)TextureMinFilter.Linear, minFilter);
        Assert.Equal((int)TextureMagFilter.Linear, magFilter);
        Assert.Equal((int)TextureWrapMode.ClampToEdge, wrapS);
        Assert.Equal((int)TextureWrapMode.ClampToEdge, wrapT);

        tex.Unbind(unit);
        Assert.Equal(0, GlStateCache.Current.GetBoundSampler(unit));
    }

    [Fact]
    public void BindScope_RestoresPreviousTextureAndSampler()
    {
        fixture.MakeCurrent();

        using var t1 = Texture2D.Create(4, 4, PixelInternalFormat.Rgba8, filter: TextureFilterMode.Nearest, debugName: "Test.T1");
        using var t2 = Texture2D.Create(4, 4, PixelInternalFormat.Rgba8, filter: TextureFilterMode.Linear, debugName: "Test.T2");

        const int unit = 2;
        t1.Bind(unit);

        int prevTexId = GlStateCache.Current.GetBoundTexture(t1.TextureTarget, unit);
        int prevSamplerId = GlStateCache.Current.GetBoundSampler(unit);
        Assert.Equal(t1.TextureId, prevTexId);
        Assert.Equal(t1.SamplerId, prevSamplerId);

        using (t2.BindScope(unit))
        {
            Assert.Equal(t2.TextureId, GlStateCache.Current.GetBoundTexture(t2.TextureTarget, unit));
            Assert.Equal(t2.SamplerId, GlStateCache.Current.GetBoundSampler(unit));
        }

        Assert.Equal(t1.TextureId, GlStateCache.Current.GetBoundTexture(t1.TextureTarget, unit));
        Assert.Equal(t1.SamplerId, GlStateCache.Current.GetBoundSampler(unit));
    }
}
