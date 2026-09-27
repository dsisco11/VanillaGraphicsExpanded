using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises production mode selection, live provider discovery and attachment lifetime.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrModeLifecycleTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public PbrModeLifecycleTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Production ownership
    /// <summary>A renderer started without LumOn discovers a later provider and rejects unpublished or disabled GI.</summary>
    [Fact]
    public void LateProviderAndModeChangesDoNotConsumeStaleLighting()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var programs = new RuntimeLightingPrograms();
        using var drawing = new ShaderTestFramework();
        using var terrain = new EngineTerrainBuffers(1, 1);
        var events = new RuntimeRenderEvents();
        var framebuffers = Enumerable.Repeat<FrameBufferRef>(null!, Enum.GetValues<EnumFrameBuffer>().Max(v => (int)v) + 1).ToList();
        framebuffers[(int)EnumFrameBuffer.Primary] = terrain.Primary;
        float[] identity = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
        var render = RuntimeEngineServices.Render(1, framebuffers, () => identity, () => identity,
            () => drawing.RenderQuad(GL.GetInteger(GetPName.CurrentProgram)));
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name switch
        {
            "get_Render" => render, "get_Event" => events.Api, "get_Shader" => programs.Api,
            _ => method.Invoke(assets.Api, args)
        });
        programs.Initialize(api);
        var config = new VgeConfig();
        config.LumOn.Enabled = false; config.LumOn.EnablePbrComposite = false;
        config.LumOn.Intensity = 1; config.LumOn.IndirectTint = [1,1,1];
        using var gbuffer = new GBufferManager(api);
        Assert.True(gbuffer.EnsureBuffers(1,1));
        using var direct = new DirectLightingBufferManager(api);
        Assert.True(direct.EnsureBuffers(1,1));
        direct.DirectDiffuseTex!.TryClearToZero(); direct.DirectSpecularTex!.TryClearToZero(); direct.EmissiveTex!.TryClearToZero();
        LumOnBufferManager? provider = null;
        int providerReads = 0;
        using var composite = new PBRCompositeRenderer(api, gbuffer, direct, config, () => { providerReads++; return provider; });
        using var later = new LumOnBufferManager(api, config);

        /// <summary>Restores authored albedo because display resolve overwrites the engine primary color.</summary>
        float Compose()
        {
            terrain.UploadTerrain(gbuffer, [.25f], [.5f,.5f,1,1], [.5f,0,0,0], [.5f,.5f,.5f,1]);
            GL.ClearTexImage(gbuffer.EnvironmentTextureId, 0, PixelFormat.Rgba, PixelType.Float, new[] { .5f,.5f,.5f,1f });
            composite.OnRenderFrame(.016f, EnumRenderStage.Opaque);
            return composite.SceneLinearColor!.ReadPixels()[0];
        }

        Assert.InRange(Compose(), .249f, .251f);
        Assert.Equal(0, providerReads);
        var shader = VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRCompositeShaderProgram>(api, "pbr_composite")!;
        Assert.Equal("0", shader.InstalledSettings!.Values["VGE_LUMON_ENABLED"].Canonical);
        config.LumOn.Enabled = true;
        Assert.InRange(Compose(), 0, .001f);
        provider = later;
        Assert.False(later.EnsureBuffers(1,1));
        Assert.True(later.EnsureBuffers(1,1));
        GL.ClearTexImage(later.IndirectFullTex!.TextureId, 0, PixelFormat.Rgba, PixelType.Float, new[] { .75f,.75f,.75f,1f });
        Assert.False(later.HasPublishedIndirect);
        Assert.InRange(Compose(), 0, .001f);
        later.HasPublishedIndirect = true;
        Assert.InRange(Compose(), .374f, .376f);
        later.RequestRecreateBuffers("lighting mode lifecycle test");
        Assert.False(later.HasPublishedIndirect);
        Assert.InRange(Compose(), 0, .001f);
        later.HasPublishedIndirect = true;
        later.ClearHistory();
        Assert.False(later.HasPublishedIndirect);
        Assert.InRange(Compose(), 0, .001f);
        config.LumOn.Enabled = false;
        int previousReads = providerReads;
        Assert.InRange(Compose(), .249f, .251f);
        Assert.Equal(previousReads, providerReads);
        shader.InvalidateAssets();
        Assert.InRange(Compose(), .249f, .251f);
        Assert.Equal("0", shader.InstalledSettings!.Values["VGE_LUMON_ENABLED"].Canonical);

        Assert.True(gbuffer.EnsureBuffers(2,2));
        Assert.Equal(gbuffer.EnvironmentTextureId, terrain.Primary.ColorTextureIds[7]);
        Assert.NotEqual(0, gbuffer.EnvironmentTextureId);
        GL.ClearTexImage(gbuffer.EnvironmentTextureId, 0, PixelFormat.Rgba, PixelType.Float, new[] { 1f,1f,1f,1f });
        gbuffer.ClearGBuffer(EnumFrameBuffer.Primary);
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, gbuffer.EnvironmentTextureId))
        {
            float[] pixels = new float[16];
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, pixels);
            Assert.All(pixels, value => Assert.Equal(0f, value));
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
