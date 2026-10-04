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
        int drawCalls = 0;
        var render = RuntimeEngineServices.Render(1, framebuffers, () => identity, () => identity,
            () => { drawCalls++; drawing.RenderQuad(GL.GetInteger(GetPName.CurrentProgram)); });
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
        int engineAttachmentCount = terrain.Primary.ColorTextureIds.Length;
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

        // Readiness prepares real binary programs and attachments without submitting either draw.
        terrain.Color.UploadDataImmediate([.125f, .25f, .5f, 1f]);
        float[] primaryBefore = terrain.Color.ReadPixels();
        using (var otherColor = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba16f))
        using (var other = GpuFramebuffer.CreateSingle(otherColor)!)
        using (StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, terrain.Output.FboId))
        using (StateCache.Current.BindFramebufferScope(FramebufferTarget.DrawFramebuffer, other.FboId))
        {
            foreach (bool enabled in new[] { false, true, false })
            {
                config.LumOn.Enabled = enabled;
                Assert.True(composite.PrepareFrame());
                var prepared = VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRCompositeShaderProgram>(api, "pbr_composite")!;
                Assert.Equal(enabled ? "1" : "0", prepared.InstalledSettings!.Values["VGE_LUMON_ENABLED"].Canonical);
            }
            Assert.Equal(terrain.Output.FboId, StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            Assert.Equal(other.FboId, StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer));
            Assert.Equal(0, drawCalls);
            Assert.False(composite.RefractionScene.Published);
            Assert.Equal(primaryBefore, terrain.Color.ReadPixels());
            Assert.True(composite.SceneLinearColor?.IsValid);

            // Invalid publications must be rejected before allocations or submissions.
            framebuffers[(int)EnumFrameBuffer.Primary] = null!;
            Assert.False(composite.PrepareFrame());
            framebuffers[(int)EnumFrameBuffer.Primary] = terrain.Primary;
            terrain.Primary.Width = 0;
            Assert.False(composite.PrepareFrame());
            terrain.Primary.Width = 1;
            terrain.Primary.DepthTextureId = 0;
            Assert.False(composite.PrepareFrame());
            terrain.Primary.DepthTextureId = terrain.Depth.TextureId;
            Assert.Equal(0, drawCalls);
            Assert.Equal(primaryBefore, terrain.Color.ReadPixels());
        }
        Assert.InRange(Compose(), .249f, .251f);
        Assert.Equal(0, providerReads);
        // Repeated production draws retain the borrowed resolve FBO until its source changes.
        var resolveField = HarmonyLib.AccessTools.Field(typeof(PBRCompositeRenderer), "primaryResolveFbo");
        var firstResolve = Assert.IsType<GpuFramebuffer>(resolveField.GetValue(composite));
        Assert.InRange(Compose(), .249f, .251f);
        Assert.Same(firstResolve, resolveField.GetValue(composite));
        using (var replacement = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba16f))
        {
            replacement.UploadDataImmediate(new float[] { .5f, .5f, .5f, 1f });
            terrain.Primary.ColorTextureIds[0] = replacement.TextureId;
            Assert.InRange(Compose(), .249f, .251f);
            var replacedResolve = Assert.IsType<GpuFramebuffer>(resolveField.GetValue(composite));
            Assert.NotSame(firstResolve, replacedResolve);
            Assert.False(firstResolve.IsValid);
            Assert.True(replacement.ReadPixels()[0] > 0f);
            terrain.Primary.ColorTextureIds[0] = terrain.Color.TextureId;
            Assert.InRange(Compose(), .249f, .251f);
            Assert.False(replacedResolve.IsValid);
            Assert.True(replacement.IsValid);
        }
        var beforeResize = Assert.IsType<GpuFramebuffer>(resolveField.GetValue(composite));
        HarmonyLib.AccessTools.Method(typeof(PBRCompositeRenderer), "OnScreenResized").Invoke(composite, null);
        Assert.False(beforeResize.IsValid);
        Assert.Null(resolveField.GetValue(composite));
        Assert.InRange(Compose(), .249f, .251f);
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
        // Re-publish the retained absent input after a live provider was withdrawn.
        using (shader.UseScope())
        {
            int previousUnit = GL.GetInteger(GetPName.ActiveTexture);
            GL.ActiveTexture(TextureUnit.Texture3);
            Assert.Equal(0, GL.GetInteger(GetPName.TextureBinding2D));
            GL.ActiveTexture((TextureUnit)previousUnit);
        }
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
        Assert.Equal(engineAttachmentCount, terrain.Primary.ColorTextureIds.Length);
        Assert.DoesNotContain(gbuffer.EnvironmentTextureId, terrain.Primary.ColorTextureIds);
        Assert.NotEqual(0, gbuffer.EnvironmentTextureId);
        GL.ClearTexImage(gbuffer.EnvironmentTextureId, 0, PixelFormat.Rgba, PixelType.Float, new[] { 1f,1f,1f,1f });
        gbuffer.ClearGBuffer(EnumFrameBuffer.Primary);
        using (StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, gbuffer.EnvironmentTextureId))
        {
            float[] pixels = new float[16];
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, pixels);
            Assert.All(pixels, value => Assert.Equal(0f, value));
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
