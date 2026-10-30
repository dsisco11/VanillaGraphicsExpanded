using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
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
        using var declarations = new StaticPassDeclarationCapture();
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
        // These renderer-level fixtures own their camera snapshot instead of running the world's publication callback.
        using var frameCamera=TestFrameCamera.CreateIdentity(1,1);
        using var frameLighting=new TestFrameLightingPublication();
        VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Get<PBRCompositeShaderProgram>(api,"pbr_composite")!.FrameInputs=frameCamera;
        VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Get<PBRCompositeShaderProgram>(api,PBRCompositeShaderProgram.PreOverlayPassName)!.FrameInputs=frameCamera;
        frameLighting.Attach(VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Get<PBRCompositeShaderProgram>(api,"pbr_composite")!);
        frameLighting.Attach(VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Get<PBRCompositeShaderProgram>(api,PBRCompositeShaderProgram.PreOverlayPassName)!);
        var config = new VgeConfig();
        config.LumOn.Enabled = false; config.LumOn.EnablePbrComposite = false;
        config.LumOn.Intensity = 1; config.LumOn.IndirectTint = [1,1,1];
        using var gbuffer = new GBufferManager(api);
        Assert.True(gbuffer.EnsureBuffers(1,1));
        int engineAttachmentCount = terrain.Primary.ColorTextureIds.Length;
        using var direct = new DirectLightingBufferManager(api);
        Assert.True(direct.EnsureBuffers(1,1));
        direct.Radiance!.UploadDataImmediate(new float[12], 0, 0, 0, 1, 1, 3);
        LumOnBufferManager? provider = null;
        int providerReads = 0;
        using var composite = new PBRCompositeRenderer(api, gbuffer, direct, config, () => { providerReads++; return provider; });
        using var later = new LumOnBufferManager(api, config);
        using var localExposure = new LocalExposureRenderer(); localExposure.FrameLightingInputs = frameLighting.Buffer;

        /// <summary>Restores authored albedo because display resolve overwrites the engine primary color.</summary>
        float Compose()
        {
            // White dielectric albedo makes native diffuse plus specular equal incident
            // irradiance regardless of the compensated microfacet split. GI retains its
            // authored half-albedo control so the provider contribution remains .375.
            float albedo = config.LumOn.Enabled ? .5f : 1f;
            float environment = config.LumOn.Enabled ? .5f : .25f;
            terrain.UploadTerrain(gbuffer, [.25f], [.5f,.5f,1,1], [.5f,0,0,0], [albedo,albedo,albedo,1]);
            gbuffer.SurfaceTexture!.UploadDataImmediate([environment,environment,environment,1f], 0, 0, 2, 1, 1, 1);
            composite.OnRenderFrame(.016f, EnumRenderStage.Opaque);
            // Consume the actual completed native/GI composition, not an invented mode label.
            float[] physical = composite.SceneLinearColor!.ReadPixels();
            var localBase = localExposure.Render(api, composite.SceneLinearColor, new(true, 1, 1, 1, .5f, .5f, 4, 4, false));
            float[] spatial = localBase.ReadPixels();
            double luminance = .2126 * physical[0] + .7152 * physical[1] + .0722 * physical[2];
            if (luminance > 0)
            {
                Assert.Equal(1, spatial[1]);
                Assert.InRange(Math.Abs(spatial[0] - Math.Log2(luminance)), 0, .00001);
            }
            else Assert.Equal(new float[] { 0, 0 }, spatial);
            return physical[0];
        }

        // Readiness prepares real binary programs and attachments without submitting either draw.
        terrain.Color.UploadDataImmediate([.125f, .25f, .5f, 1f]);
        float[] primaryBefore = terrain.Color.ReadPixels();
        using (var otherColor = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource())
        using (var other = DynamicGpuFramebuffer.CreateSingle(otherColor)!)
        using (OpenGlStateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, terrain.Output.FboId))
        using (OpenGlStateCache.Current.BindFramebufferScope(FramebufferTarget.DrawFramebuffer, other.FboId))
        {
            foreach (bool enabled in new[] { false, true, false })
            {
                config.LumOn.Enabled = enabled;
                Assert.True(composite.PrepareFrame());
                var prepared = VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Get<PBRCompositeShaderProgram>(api, "pbr_composite")!;
                Assert.Equal(enabled ? "1" : "0", prepared.InstalledSettings!.Values["VGE_LUMON_ENABLED"].Canonical);
            }
            Assert.Equal(terrain.Output.FboId, OpenGlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            Assert.Equal(other.FboId, OpenGlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer));
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
        // Repeated production draws reuse attachment intentions until their borrowed source changes.
        var resolveField = HarmonyLib.AccessTools.Field(typeof(PBRCompositeRenderer), "primaryResolvePass");
        var firstResolve = Assert.IsType<RHIRenderPassDescription>(resolveField.GetValue(composite));
        Assert.Equal(terrain.Color.TextureId, firstResolve.Colors[0].Image!.Value.Texture.TextureId);
        Assert.InRange(Compose(), .249f, .251f);
        Assert.Same(firstResolve, resolveField.GetValue(composite));
        using (var replacement = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource())
        {
            replacement.UploadDataImmediate(new float[] { 1f, 1f, 1f, 1f });
            // Simulate the engine's native replacement and its existing managed publication boundary.
            GL.NamedFramebufferTexture(terrain.Primary.FboId, FramebufferAttachment.ColorAttachment0, replacement.TextureId, 0);
            terrain.Primary.ColorTextureIds[0] = replacement.TextureId;
            gbuffer.SetupGBuffers();
            Assert.InRange(Compose(), .249f, .251f);
            var replacedResolve = Assert.IsType<RHIRenderPassDescription>(resolveField.GetValue(composite));
            Assert.NotSame(firstResolve, replacedResolve);
            Assert.False(firstResolve.Colors[0].Image!.Value.Texture.IsValid);
            Assert.True(terrain.Color.IsValid);
            Assert.Equal(replacement.TextureId, replacedResolve.Colors[0].Image!.Value.Texture.TextureId);
            Assert.True(replacement.ReadPixels()[0] > 0f);
            GL.NamedFramebufferTexture(terrain.Primary.FboId, FramebufferAttachment.ColorAttachment0, terrain.Color.TextureId, 0);
            terrain.Primary.ColorTextureIds[0] = terrain.Color.TextureId;
            gbuffer.SetupGBuffers();
            Assert.InRange(Compose(), .249f, .251f);
            Assert.False(replacedResolve.Colors[0].Image!.Value.Texture.IsValid);
            Assert.True(replacement.IsValid);
        }
        var beforeResize = Assert.IsType<RHIRenderPassDescription>(resolveField.GetValue(composite));
        HarmonyLib.AccessTools.Method(typeof(PBRCompositeRenderer), "OnScreenResized").Invoke(composite, null);
        Assert.True(beforeResize.Colors[0].Image!.Value.Texture.IsValid);
        Assert.Null(resolveField.GetValue(composite));
        Assert.InRange(Compose(), .249f, .251f);
        Assert.NotSame(beforeResize, resolveField.GetValue(composite));
        var shader = VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Get<PBRCompositeShaderProgram>(api, "pbr_composite")!;
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
        using (var bindingPipeline2 = ShaderBindingTestPipeline.Create(shader))
            {
                RHICommandContext.Current.SetPipeline(bindingPipeline2);
                RHICommandContext.Current.BindResources(shader.CaptureBindings());
            int previousUnit = GL.GetInteger(GetPName.ActiveTexture);
            GL.ActiveTexture(TextureUnit.Texture4);
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
        Assert.DoesNotContain(gbuffer.SurfaceTextureId, terrain.Primary.ColorTextureIds);
        Assert.NotEqual(0, gbuffer.SurfaceTextureId);
        gbuffer.SurfaceTexture!.UploadDataImmediate(Enumerable.Repeat(1f, 16).ToArray(), 0, 0, 2, 2, 2, 1);
        gbuffer.ClearGBuffer(EnumFrameBuffer.Primary);
        Assert.All(LayeredTestTexture.Read(gbuffer.SurfaceTexture!, 2), value => Assert.Equal(0f, value));
        declarations.AssertReusable();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
