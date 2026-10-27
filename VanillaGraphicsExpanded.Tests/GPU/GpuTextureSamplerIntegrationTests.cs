using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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

        using var tex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test.Tex" }).RequireResource();

        Assert.True(tex.IsValid);

        const int unit = 3;
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(unit, tex, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(((GpuTextureRef)tex).Target))]);

        Assert.Equal(0, StateCache.Current.GetBoundSampler(unit));

        using (StateCache.Current.BindTextureScope(tex.TextureTarget, unit: 0, tex.TextureId))
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

        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(unit, default, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(((GpuTextureRef)tex).Target))]);
        Assert.Equal(0, StateCache.Current.GetBoundSampler(unit));
    }

    [Fact]
    public void BindScope_RestoresPreviousTextureAndSampler()
    {
        fixture.MakeCurrent();

        using var t1 = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test.T1" }).RequireResource();
        using var t2 = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test.T2" }).RequireResource();
        using var s1 = RHICommandContext.Current.CreateSampler(new RHISamplerDescription { Label = "Test.S1" }).RequireResource();

        const int unit = 2;

        // Establish a known texture+sampler binding without going through GpuTexture.Bind (which unbinds samplers).
        OpenGLImageBindings.BindTexture(StateCache.Current, t1.TextureTarget, unit, t1.TextureId);
        OpenGLImageBindings.BindSampler(StateCache.Current, unit, s1);

        int prevTexId = StateCache.Current.GetBoundTexture(t1.TextureTarget, unit);
        int prevSamplerId = StateCache.Current.GetBoundSampler(unit);
        Assert.Equal(t1.TextureId, prevTexId);
        Assert.Equal(s1.SamplerId, prevSamplerId);

        using (StateCache.Current.BindSamplerScope(unit, 0))
        using (StateCache.Current.BindTextureScope(((GpuTextureRef)t2).Target, unit, t2.TextureId))
        {
            Assert.Equal(t2.TextureId, StateCache.Current.GetBoundTexture(t2.TextureTarget, unit));
            Assert.Equal(0, StateCache.Current.GetBoundSampler(unit));
        }

        Assert.Equal(t1.TextureId, StateCache.Current.GetBoundTexture(t1.TextureTarget, unit));
        Assert.Equal(s1.SamplerId, StateCache.Current.GetBoundSampler(unit));
    }
}
