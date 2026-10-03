using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Verifies pre-overlay capture preserves engine draw ownership and actual GL state.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionCaptureStateTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Runs actual direct/composite capture while engine bindings differ from cached framebuffer values.</summary>
    [Fact]
    public void CaptureRestoresEngineOwnerAndIndependentFramebufferBindings()
    {
        EnsureContextValid();
        using var fixedFunction = StateCache.Current.CaptureLegacyFixedFunctionState();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var programs = new RuntimeLightingPrograms();
        using var drawing = new ShaderTestFramework();
        using var terrain = new EngineTerrainBuffers(1, 1);
        var events = new RuntimeRenderEvents();
        var framebuffers = Enumerable.Repeat<FrameBufferRef>(null!, Enum.GetValues<EnumFrameBuffer>().Max(v => (int)v) + 1).ToList();
        framebuffers[(int)EnumFrameBuffer.Primary] = terrain.Primary;
        float[] identity = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
        int draws = 0;
        var render = RuntimeEngineServices.Render(1, framebuffers, () => identity, () => identity,
            () => { draws++; drawing.RenderQuad(GL.GetInteger(GetPName.CurrentProgram)); });
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
        using var directBuffers = new DirectLightingBufferManager(api);
        using var direct = new DirectLightingRenderer(api, gbuffer, directBuffers);
        using var composite = new PBRCompositeRenderer(api, gbuffer, directBuffers, config, () => null);
        using var capture = new VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture(api, direct, composite);
        using var callerTarget = CreateRenderTarget(2,2,PixelInternalFormat.Rgba32f);
        using var readTarget = CreateRenderTarget(1,1,PixelInternalFormat.Rgba32f);
        terrain.UploadTerrain(gbuffer, [.75f], [.5f,.5f,1,1], [.5f,0,0,0], [.5f,.5f,.5f,1]);
        var caller = VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRDisplayResolveShaderProgram>(api, "pbr_display_resolve")!;
        caller.PrimaryScene = terrain.Color.TextureId;
        caller.PrimaryDepth = terrain.Depth.TextureId;
        bool prior = VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled;
        try
        {
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled = true;
            caller.Use();
            // Simulate engine framebuffer binds that bypass an already-primed VGE cache.
            StateCache.Current.BindFramebuffer(FramebufferTarget.Framebuffer, terrain.Output.FboId);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, callerTarget.FboId);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readTarget.FboId);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.Viewport(1,1,1,1);
            GL.DepthMask(false);
            GL.Enable(EnableCap.Blend);
            HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null);
            Assert.True(composite.PreOverlayScene!.Published);
            Assert.Equal(2, draws);
            Assert.Equal(callerTarget.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(readTarget.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(caller.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Same(caller, Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
            Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
            Assert.True(GL.IsEnabled(EnableCap.Blend));
            int[] viewport = new int[4];
            GL.GetInteger(GetPName.Viewport, viewport);
            Assert.Equal(new[] {1,1,1,1}, viewport);
            Assert.Equal(.75f, composite.PreOverlayScene.Depth!.ReadPixels()[0]);
            Assert.Equal(1f, composite.PreOverlayScene.Color!.ReadPixels()[3]);
            Assert.Equal(.75f, terrain.Depth.ReadPixels()[0]);
            Assert.Equal(new float[] {.5f,.5f,.5f,1f}, terrain.Color.ReadPixels());
            // A resize must withdraw the borrowed pre-overlay publication before its scratch image changes.
            HarmonyLib.AccessTools.Method(typeof(PBRCompositeRenderer), "OnScreenResized").Invoke(composite, null);
            Assert.False(composite.PreOverlayScene.Published);
            HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null);
            Assert.True(composite.PreOverlayScene.Published);
            var capturedColor = composite.PreOverlayScene.Color;
            var capturedDepth = composite.PreOverlayScene.Depth;
            events.LeaveWorld();
            Assert.False(composite.PreOverlayScene.Published);
            Assert.Null(composite.PreOverlayScene.Color);
            Assert.Null(composite.PreOverlayScene.Depth);
            Assert.False(capturedColor!.IsValid);
            Assert.False(capturedDepth!.IsValid);
            HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null);
            Assert.True(composite.PreOverlayScene.Published);
            capture.OnRenderFrame(.016f, EnumRenderStage.Before);
            Assert.False(composite.PreOverlayScene.Published);
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled = false;
            capture.OnRenderFrame(.016f, EnumRenderStage.Before);
            Assert.Null(composite.PreOverlayScene.Color);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            caller.Stop();
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled = prior;
        }
    }
    #endregion
}

