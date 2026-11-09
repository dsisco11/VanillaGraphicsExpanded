using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>
/// Integration tests verifying that <see cref="GpuTexture"/> uses texture-object parameters (TexParameter)
/// and unbinds any sampler object that would override them.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuTextureSamplerIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    #region Public API

    /// <summary>Uses the shared headless OpenGL fixture for sampler integration scenarios.</summary>
    public GpuTextureSamplerIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>Binding a texture clears a native sampler override and preserves the texture object's filter and wrap parameters.</summary>
    [Fact]
    public void Texture2D_Bind_UnbindsSampler_AndHasExpectedTextureObjectParameters()
    {
        // Arrange: Create a linear texture and bind a sampler sentinel that would override its object parameters.
        fixture.MakeCurrent();

        using var tex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test.Tex" }).RequireResource();

        Assert.True(tex.IsValid);

        const int unit = 3;
        using var sampler = RHICommandContext.Current.CreateSampler(new RHISamplerDescription { Label = "Test.SamplerSentinel" }).RequireResource();
        OpenGLImageBindings.BindSampler(OpenGlStateCache.Current, unit, sampler);
        GL.GetInteger((GetIndexedPName)All.SamplerBinding, unit, out int samplerBeforeBind);
        Assert.Equal(sampler.SamplerId, samplerBeforeBind);

        // Act: Bind the texture through the sampled-resource API, which must clear the sampler override.
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(unit, tex, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)tex).Target)))]);

        // Assert: The override is unbound and the texture object's filter/wrap parameters remain configured.
        Assert.Equal(0, OpenGlStateCache.Current.GetBoundSampler(unit));
        GL.GetInteger((GetIndexedPName)All.SamplerBinding, unit, out int samplerAfterBind);
        Assert.Equal(0, samplerAfterBind);

        using (OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(tex.TextureTarget), unit: 0, tex.TextureId))
        {
            GL.GetTexParameter(OpenGLImageValues.Target(tex.TextureTarget), GetTextureParameter.TextureMinFilter, out int minFilter);
            GL.GetTexParameter(OpenGLImageValues.Target(tex.TextureTarget), GetTextureParameter.TextureMagFilter, out int magFilter);
            GL.GetTexParameter(OpenGLImageValues.Target(tex.TextureTarget), GetTextureParameter.TextureWrapS, out int wrapS);
            GL.GetTexParameter(OpenGLImageValues.Target(tex.TextureTarget), GetTextureParameter.TextureWrapT, out int wrapT);

            Assert.Equal((int)TextureMinFilter.Linear, minFilter);
            Assert.Equal((int)TextureMagFilter.Linear, magFilter);
            Assert.Equal((int)TextureWrapMode.ClampToEdge, wrapS);
            Assert.Equal((int)TextureWrapMode.ClampToEdge, wrapT);
        }

        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(unit, default, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)tex).Target)))]);
        Assert.Equal(0, OpenGlStateCache.Current.GetBoundSampler(unit));
        GL.GetInteger((GetIndexedPName)All.SamplerBinding, unit, out int samplerAfterClear);
        Assert.Equal(0, samplerAfterClear);
    }

    /// <summary>A texture and sampler binding scope restores the prior texture and sampler values reported by the state cache.</summary>
    [Fact]
    public void BindScope_RestoresPreviousTextureAndSampler()
    {
        // Arrange: Establish a known texture and sampler pair before entering temporary binding scopes.
        fixture.MakeCurrent();

        using var t1 = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test.T1" }).RequireResource();
        using var t2 = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test.T2" }).RequireResource();
        using var s1 = RHICommandContext.Current.CreateSampler(new RHISamplerDescription { Label = "Test.S1" }).RequireResource();

        const int unit = 2;

        // Establish a known texture+sampler binding without going through GpuTexture.Bind (which unbinds samplers).
        OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, OpenGLImageValues.Target(t1.TextureTarget), unit, t1.TextureId);
        OpenGLImageBindings.BindSampler(OpenGlStateCache.Current, unit, s1);

        int prevTexId = OpenGlStateCache.Current.GetBoundTexture(OpenGLImageValues.Target(t1.TextureTarget), unit);
        int prevSamplerId = OpenGlStateCache.Current.GetBoundSampler(unit);
        Assert.Equal(t1.TextureId, prevTexId);
        Assert.Equal(s1.SamplerId, prevSamplerId);

        // Act: Bind a different texture with sampler zero, then leave both scopes.
        using (OpenGlStateCache.Current.BindSamplerScope(unit, 0))
        using (OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)t2).Target), unit, t2.TextureId))
        {
            Assert.Equal(t2.TextureId, OpenGlStateCache.Current.GetBoundTexture(OpenGLImageValues.Target(t2.TextureTarget), unit));
            Assert.Equal(0, OpenGlStateCache.Current.GetBoundSampler(unit));
        }

        // Assert: Scope exit restores both original bindings on the same texture unit.
        Assert.Equal(t1.TextureId, OpenGlStateCache.Current.GetBoundTexture(OpenGLImageValues.Target(t1.TextureTarget), unit));
        Assert.Equal(s1.SamplerId, OpenGlStateCache.Current.GetBoundSampler(unit));
    }
    #endregion
}
