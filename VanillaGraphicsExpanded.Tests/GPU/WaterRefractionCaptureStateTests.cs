using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
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
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void CaptureRestoresEngineOwnerAndIndependentFramebufferBindings(int backgroundScale, bool lumon)
    {
        EnsureContextValid();
        using var fixedFunction = LegacyFixedFunctionReference.Capture(StateCache.Current);
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var programs = new RuntimeLightingPrograms();
        using var drawing = new ShaderTestFramework();
        using var marker = new FirstPersonMarkerDraw();
        using var terrain = new EngineTerrainBuffers(1, 1);
        var events = new RuntimeRenderEvents();
        var framebuffers = Enumerable.Repeat<FrameBufferRef>(null!, Enum.GetValues<EnumFrameBuffer>().Max(v => (int)v) + 1).ToList();
        framebuffers[(int)EnumFrameBuffer.Primary] = terrain.Primary;
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        int draws = 0; bool failDraw = false; Action? duringDraw = null;
        var render = RuntimeEngineServices.Render(1, framebuffers, () => identity, () => identity,
            () => { if (failDraw) throw new InvalidOperationException("Injected engine mesh failure"); draws++; duringDraw?.Invoke(); drawing.RenderGeometry(); });
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name switch
        {
            "get_Render" => render,
            "get_Event" => events.Api,
            "get_Shader" => programs.Api,
            _ => method.Invoke(assets.Api, args)
        });
        programs.Initialize(api);
        // These renderer-level fixtures own their camera snapshot instead of running the world's publication callback.
        using var frameCamera=TestFrameCamera.CreateIdentity(1,1);
        VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRCompositeShaderProgram>(api,"pbr_composite")!.FrameInputs=frameCamera;
        VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRCompositeShaderProgram>(api,PBRCompositeShaderProgram.PreOverlayPassName)!.FrameInputs=frameCamera;
        using var lights=new VgeLightsUniformBuffer();
        var directProgram=GpuShaderPrograms.Get<PBRDirectLightingShaderProgram>(api,"pbr_direct_lighting")!;
        directProgram.FrameInputs=frameCamera;directProgram.LightsInputs=lights;
        var config = new VanillaGraphicsExpanded.LumOn.VgeConfig();
        config.LumOn.Enabled = lumon; config.LumOn.EnablePbrComposite = false;
        config.LumOn.Intensity = 1; config.LumOn.IndirectTint = [1, 1, 1];
        using var gbuffer = new GBufferManager(api);
        Assert.True(gbuffer.EnsureBuffers(1, 1));
        using var directBuffers = new DirectLightingBufferManager(api);
        using var direct = new DirectLightingRenderer(api, gbuffer, directBuffers);
        using var composite = new PBRCompositeRenderer(api, gbuffer, directBuffers, config, () => null);
        using var capture = new VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture(api, direct, composite);
        using var callerTarget = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var readTarget = CreateRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        terrain.UploadTerrain(gbuffer, [.75f], [.5f, .5f, 1, 1], [.5f, 0, 0, 0], [.5f, .5f, .5f, 1]);
        var caller = VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRDisplayResolveShaderProgram>(api, "pbr_display_resolve")!;
        caller.PrimaryScene = terrain.Color.TextureId;
        caller.PrimaryDepth = terrain.Depth.TextureId;
        bool prior = VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled;
        int priorScale = VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionBackgroundScale;
        try
        {
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled = true;
            // Persisted quality increases from half (1) to full (2); the owner uses a size divisor.
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionBackgroundScale = 3 - backgroundScale;
            // Negative control: the legacy scope broadcasts output-zero blending to metadata.
            GL.Enable(EnableCap.Blend);
            GL.Disable(IndexedEnableCap.Blend, 4);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            StateCache.Current.Invalidate(EPipelineState.Blend);
            using (LegacyFixedFunctionReference.Capture(StateCache.Current)) { }
            marker.Draw(terrain, gbuffer);
            Assert.True(marker.ReadMarker(gbuffer) >= 0, "Legacy restoration must reproduce the lost negative marker.");
            terrain.UploadTerrain(gbuffer, [.75f], [.5f, .5f, 1, 1], [.5f, 0, 0, 0], [.5f, .5f, .5f, 1]);
            caller.Use();
            // Simulate engine framebuffer binds that bypass an already-primed VGE cache.
            StateCache.Current.BindFramebuffer(FramebufferTarget.Framebuffer, terrain.Output.FboId);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, callerTarget.FboId);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readTarget.FboId);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.Viewport(1, 1, 1, 1);
            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            for (int output = 0; output < 8; output++)
                if (output < 3) GL.Enable(IndexedEnableCap.Blend, output);
                else GL.Disable(IndexedEnableCap.Blend, output);
            StateCache.Current.Invalidate(EPipelineState.Depth | EPipelineState.Blend | EPipelineState.Viewport | EPipelineState.FramebufferBindings);
            using (var hostile = new HostileFullscreenState())
            {
                HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null);
                hostile.AssertRestored();
            }
            Assert.True(composite.PreOverlayScene!.Published);
            Assert.Equal(1, composite.PreOverlayScene.BackgroundScale);
            Assert.Equal(2, draws);
            Assert.Null(composite.SceneLinearColor);
            Assert.Equal(callerTarget.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(readTarget.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(caller.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Same(caller, Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
            Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
            Assert.True(GL.IsEnabled(EnableCap.DepthTest));
            for (int output = 0; output < 8; output++)
                Assert.Equal(output < 3, GL.IsEnabled(IndexedEnableCap.Blend, output));
            Assert.True(GL.IsEnabled(EnableCap.Blend));
            int[] viewport = new int[4];
            GL.GetInteger(GetPName.Viewport, viewport);
            Assert.Equal(new[] { 1, 1, 1, 1 }, viewport);
            Assert.Equal(.75f, composite.PreOverlayScene.Depth!.ReadPixels()[0]);
            Assert.Equal(1f, composite.PreOverlayScene.Color!.ReadPixels()[3]);
            Assert.Equal(.75f, terrain.Depth.ReadPixels()[0]);
            Assert.Equal(new float[] { .5f, .5f, .5f, 1f }, terrain.Color.ReadPixels());
            // Simulate the first-person write after clean capture, then run the actual
            // final owner. Reduction must consume restored world depth, never the hand proxy.
            caller.Stop();
            Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
            StateCache.Current.Invalidate(EPipelineState.Program);
            marker.Draw(terrain, gbuffer);
            Assert.Equal(-1f, marker.ReadMarker(gbuffer));
            direct.OnRenderFrame(.016f, EnumRenderStage.Opaque);
            composite.OnRenderFrame(.016f, EnumRenderStage.Opaque);
            Assert.True(composite.RefractionScene.Published);
            Assert.Null(Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
            Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Equal(backgroundScale, composite.RefractionScene.BackgroundScale);
            Assert.Equal(backgroundScale == 2 ? 6 : 5, draws);
            Assert.Equal(.75f, composite.RefractionScene.SourceDepth!.ReadPixels()[0]);
            Assert.Equal(.75f, composite.RefractionScene.Depth!.ReadPixels()[0]);
            Assert.Equal(composite.PreOverlayScene.Color!.ReadPixels(), composite.RefractionScene.SourceColor!.ReadPixels());
            Assert.Equal(composite.RefractionScene.SourceColor.ReadPixels(), composite.RefractionScene.Color!.ReadPixels());
            if (backgroundScale == 2)
                Assert.Equal(new float[] { .75f, .5f, .5f, 1 }, composite.RefractionScene.Depth.ReadPixels());
            Assert.Equal(.01f, terrain.Depth.ReadPixels()[0]);

            var ordinary = GpuShaderPrograms.Get<PBRCompositeShaderProgram>(api, "pbr_composite")!;
            var preOverlay = GpuShaderPrograms.Get<PBRCompositeShaderProgram>(api, PBRCompositeShaderProgram.PreOverlayPassName)!;
            Assert.NotSame(ordinary, preOverlay);
            Assert.NotEqual(ordinary.ProgramId, preOverlay.ProgramId);
            int retainedCapture = preOverlay.ProgramId;
            // Structural changes belong to the ordinary owner. Warm each selected
            // setting once, then verify actual alternating draws reuse both programs.
            foreach (var (enabled, pbr, ao) in new[]
                { (lumon, lumon, lumon), (!lumon, !lumon, !lumon), (true, true, false), (true, false, true), (lumon, lumon, lumon) })
            {
                config.LumOn.Enabled = enabled;
                config.LumOn.EnablePbrComposite = pbr;
                config.LumOn.EnableShortRangeAo = ao;
                Assert.True(composite.PrepareFrame());
                Assert.Equal(retainedCapture, preOverlay.ProgramId);
                Assert.Equal(enabled, ordinary.LumOnEnabled);
                Assert.Equal(enabled && pbr, ordinary.EnablePbrComposite);
                Assert.Equal(enabled && ao, ordinary.EnableShortRangeAo);
                Assert.False(preOverlay.LumOnEnabled);
                Assert.False(preOverlay.EnablePbrComposite);
                Assert.False(preOverlay.EnableShortRangeAo);
                Assert.True(preOverlay.PreOverlayOnly);
                Assert.False(ordinary.PreOverlayOnly);
                AssertStableFrames();
            }

            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled = false;
            capture.OnRenderFrame(.016f, EnumRenderStage.Before);
            int ordinaryId = ordinary.ProgramId;
            int readsBeforeDisabled = assets.Reads.Count;
            for (int frame = 0; frame < 2; frame++)
            {
                Assert.True(composite.PrepareFrame());
                composite.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                Assert.False(composite.RefractionScene.Published);
                Assert.False(composite.PreOverlayScene.Published);
            }
            Assert.Equal(ordinaryId, ordinary.ProgramId);
            Assert.Equal(retainedCapture, preOverlay.ProgramId);
            Assert.Equal(readsBeforeDisabled, assets.Reads.Count);
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled = true;
            AssertStableFrames();

            // An unavailable receiver target must not fall back to the ordinary
            // composite target or leave the preceding publication readable.
            var preservedColor = Assert.IsType<DynamicTexture2D>(composite.SceneLinearColor);
            float[] preservedPixels = preservedColor.ReadPixels();
            HarmonyLib.AccessTools.Field(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionScene), "failed")
                .SetValue(composite.PreOverlayScene, true);
            int beforeFailure = draws;
            HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null);
            Assert.Equal(beforeFailure + 1, draws); // Direct capture succeeds; composite refuses to submit.
            Assert.False(composite.PreOverlayScene.Published);
            Assert.Equal(preservedPixels, preservedColor.ReadPixels());
            composite.PreOverlayScene.Dispose();
            AssertStableFrames();

            // Optional entry cannot preserve an unknown foreign shader owner; it withdraws
            // publication without submitting either pass or altering the engine binding.
            caller.Use(); caller.Stop();
            Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram = new Vintagestory.Client.NoObf.ShaderProgramParticlescube();
            int rawProgram = GL.GetInteger(GetPName.CurrentProgram);
            int beforeRejectedEntry = draws;
            HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null);
            Assert.Equal(beforeRejectedEntry, draws);
            Assert.False(composite.PreOverlayScene.Published);
            Assert.Equal(rawProgram, GL.GetInteger(GetPName.CurrentProgram));
            Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram = null;
            StateCache.Current.UnbindProgram();
            StateCache.Current.BindFramebuffer(FramebufferTarget.DrawFramebuffer, callerTarget.FboId);
            StateCache.Current.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readTarget.FboId);
            StateCache.Current.ApplyDynamic(new VanillaGraphicsExpanded.Rendering.Pipeline.State.DynamicDrawState { X = 1, Y = 1, Width = 1, Height = 1 });
            using (var hostile = new HostileFullscreenState())
            {
                failDraw = true;
                var captureFailure = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                    HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null));
                Assert.IsType<InvalidOperationException>(captureFailure.InnerException);
                Assert.False(composite.PreOverlayScene.Published);
                AssertCallbackState();
                Assert.Throws<InvalidOperationException>(() => direct.OnRenderFrame(.016f, EnumRenderStage.Opaque));
                AssertCallbackState();
                Assert.Throws<InvalidOperationException>(() => composite.OnRenderFrame(.016f, EnumRenderStage.Opaque));
                Assert.False(composite.RefractionScene.Published);
                AssertCallbackState();
                hostile.AssertRestored();
            }
            failDraw = false;
            AssertStableFrames();

            // A live empty pool is authoritative even after a previous wet frame.
            // Keep allocated targets across the skip, then republish current data
            // when geometry is added back to that same engine-owned pool.
            var liquidRenderer = (Vintagestory.Client.NoObf.ChunkRenderer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Vintagestory.Client.NoObf.ChunkRenderer));
            liquidRenderer.textureIds = [17];
            liquidRenderer.poolsByRenderPass = new MeshDataPoolManager[(int)EnumChunkRenderPass.Liquid + 1][];
            var manager = new MeshDataPoolManager(null!, null!, api, 16, 24, 4);
            liquidRenderer.poolsByRenderPass[(int)EnumChunkRenderPass.Liquid] = [manager];
            var pool = (MeshDataPool)HarmonyLib.AccessTools.Constructor(typeof(MeshDataPool), [typeof(int), typeof(int), typeof(int)]).Invoke([16, 24, 4]);
            var livePools = (List<MeshDataPool>)HarmonyLib.AccessTools.Field(typeof(MeshDataPoolManager), "pools").GetValue(manager)!;
            var locations = (List<ModelDataPoolLocation>)HarmonyLib.AccessTools.Field(typeof(MeshDataPool), "poolLocations").GetValue(pool)!;
            livePools.Add(pool);
            VanillaGraphicsExpanded.PBR.Liquids.LiquidMeshSource.Register(api, liquidRenderer, new(1, 1));
            try
            {
                var retainedColor = composite.PreOverlayScene.Color;
                var retainedDepth = composite.PreOverlayScene.Depth;
                int beforeDry = draws;
                HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null);
                Assert.Equal(beforeDry, draws);
                Assert.False(composite.PreOverlayScene.Published);
                Assert.Same(retainedColor, composite.PreOverlayScene.Color);
                Assert.Same(retainedDepth, composite.PreOverlayScene.Depth);
                Assert.True(retainedColor!.IsValid); Assert.True(retainedDepth!.IsValid);
                var location = new ModelDataPoolLocation();
                locations.Add(location);
                capture.OnRenderFrame(.016f, EnumRenderStage.Before);
                ComposeChangedWorld(.45f);
                Assert.Same(retainedColor, composite.PreOverlayScene.Color);
                Assert.Same(retainedDepth, composite.PreOverlayScene.Depth);
                pool.RemoveLocation(location);
                capture.OnRenderFrame(.016f, EnumRenderStage.Before);
                beforeDry = draws;
                HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null);
                Assert.Equal(beforeDry, draws); Assert.False(composite.PreOverlayScene.Published);
            }
            finally { VanillaGraphicsExpanded.PBR.Liquids.LiquidMeshSource.Remove(api); }

            Assert.Null(Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
            // Engine Stop releases managed ownership but can retain the last raw
            // binding. End that submission before simulating the engine reload boundary.
            StateCache.Current.UnbindProgram();
            Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
            Assert.True(VgeShaderPrograms.RegisterAll(api));
            Assert.Same(ordinary, GpuShaderPrograms.Get<PBRCompositeShaderProgram>(api, "pbr_composite"));
            Assert.Same(preOverlay, GpuShaderPrograms.Get<PBRCompositeShaderProgram>(api, PBRCompositeShaderProgram.PreOverlayPassName));
            Assert.True(composite.PrepareFrame());
            // Reload also invalidates direct/reduction programs; warm the complete
            // production path before checking for recurring executable preparation.
            ComposeChangedWorld(.375f);
            AssertStableFrames();

            terrain.UploadTerrain(gbuffer, [.75f], [.5f, .5f, 1, 1], [.5f, 0, 0, 0], [.5f, .5f, .5f, 1]);
            caller.Use();
            caller.PrimaryScene = terrain.Color.TextureId;
            caller.PrimaryDepth = terrain.Depth.TextureId;
            // A real odd-sized engine resize changes all borrowed inputs before the next capture.
            terrain.Color.Resize(3, 3); terrain.Depth.Resize(3, 3);
            terrain.Primary.Width = 3; terrain.Primary.Height = 3;
            Moq.Mock.Get(render).SetupGet(value => value.FrameWidth).Returns(3);
            Moq.Mock.Get(render).SetupGet(value => value.FrameHeight).Returns(3);
            Assert.True(gbuffer.EnsureBuffers(3, 3));
            terrain.UploadTerrain(gbuffer, Enumerable.Repeat(.75f, 9).ToArray(),
                Enumerable.Range(0, 9).SelectMany(_ => new float[] { .5f, .5f, 1, 1 }).ToArray(),
                Enumerable.Range(0, 9).SelectMany(_ => new float[] { .5f, 0, 0, 0 }).ToArray(),
                Enumerable.Range(0, 9).SelectMany(_ => new float[] { .5f, .5f, .5f, 1 }).ToArray());
            // A resize must withdraw the borrowed pre-overlay publication before its scratch image changes.
            HarmonyLib.AccessTools.Method(typeof(PBRCompositeRenderer), "OnScreenResized").Invoke(composite, null);
            Assert.False(composite.PreOverlayScene.Published);
            HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null);
            Assert.True(composite.PreOverlayScene.Published);
            Assert.Equal(3, composite.PreOverlayScene.Color!.Width);
            Assert.All(composite.PreOverlayScene.Depth!.ReadPixels(), value => Assert.Equal(.75f, value));
            var capturedColor = composite.PreOverlayScene.Color;
            var capturedDepth = composite.PreOverlayScene.Depth;
            // Retiring the incoming owner during the interruption makes shader cleanup
            // fail. Production capture must withdraw its completed pair and surface the
            // classified restoration error, rather than treating it as optional capture loss.
            duringDraw = () => { duringDraw = null; caller.Dispose(); };
            var cleanupFailure = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null));
            Assert.True(EngineBoundaryRestoreException.IsRestorationFailure(cleanupFailure.InnerException!));
            Assert.False(composite.PreOverlayScene.Published);
            caller.Stop(); StateCache.Current.UnbindProgram();
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
            caller.Stop();
            ordinaryId = ordinary.ProgramId;
            retainedCapture = preOverlay.ProgramId;
            GpuShaderPrograms.Dispose(api);
            Assert.False(GL.IsProgram(ordinaryId));
            Assert.False(GL.IsProgram(retainedCapture));
            Assert.Equal(ErrorCode.NoError, GL.GetError());

            /// <summary>Observes stable real executables and asset reads across changed frame inputs.</summary>
            void AssertStableFrames()
            {
                int ordinaryProgram = ordinary.ProgramId;
                int captureProgram = preOverlay.ProgramId;
                var ordinaryInterface = ordinary.ProgramLayout.BinaryInterface;
                var captureInterface = preOverlay.ProgramLayout.BinaryInterface;
                int reads = assets.Reads.Count;
                ComposeChangedWorld(.25f);
                ComposeChangedWorld(.625f);
                Assert.Equal(ordinaryProgram, ordinary.ProgramId);
                Assert.Equal(captureProgram, preOverlay.ProgramId);
                Assert.Same(ordinaryInterface, ordinary.ProgramLayout.BinaryInterface);
                Assert.Same(captureInterface, preOverlay.ProgramLayout.BinaryInterface);
                Assert.Equal(reads, assets.Reads.Count);
            }

            /// <summary>Checks the independent callbacks return their incoming native engine state.</summary>
            void AssertCallbackState()
            {
                Assert.Equal(callerTarget.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
                Assert.Equal(readTarget.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
                int[] rectangle = new int[4]; GL.GetInteger(GetPName.Viewport, rectangle);
                Assert.Equal(new[] { 1, 1, 1, 1 }, rectangle);
                Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
                Assert.True(GL.IsEnabled(EnableCap.DepthTest));
                for (int output = 0; output < 8; output++)
                    Assert.Equal(output < 3, GL.IsEnabled(IndexedEnableCap.Blend, output));
            }

            /// <summary>Captures new world data then verifies ordinary publication restores it behind an overlay.</summary>
            void ComposeChangedWorld(float depth)
            {
                Assert.True(composite.PrepareFrame());
                Assert.Equal(ErrorCode.NoError, GL.GetError());
                var ordinaryColor = Assert.IsType<DynamicTexture2D>(composite.SceneLinearColor);
                float[] sentinel = [13f, 7f, 3f, 1f];
                ordinaryColor.UploadDataImmediate(sentinel);
                terrain.UploadTerrain(gbuffer, [depth], [.5f, .5f, 1, 1], [.5f, 0, 0, 0], [depth, depth, depth, 1]);
                HarmonyLib.AccessTools.Method(typeof(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture), "Capture").Invoke(capture, null);
                Assert.True(composite.PreOverlayScene!.Published);
                Assert.Equal(sentinel, ordinaryColor.ReadPixels());
                Assert.Equal(depth, composite.PreOverlayScene.Depth!.ReadPixels()[0]);
                Assert.Equal(ErrorCode.NoError, GL.GetError());
                float[] worldColor = composite.PreOverlayScene.Color!.ReadPixels();
                // Independently registered callbacks must produce the same world lighting as
                // isolated capture and return the engine's separate read/draw and viewport state.
                StateCache.Current.BindFramebuffer(FramebufferTarget.DrawFramebuffer, callerTarget.FboId);
                StateCache.Current.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readTarget.FboId);
                StateCache.Current.ApplyDynamic(new VanillaGraphicsExpanded.Rendering.Pipeline.State.DynamicDrawState { X = 1, Y = 1, Width = 1, Height = 1 });
                direct.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                AssertCallbackState();
                composite.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                AssertCallbackState();
                if (!config.LumOn.Enabled)
                    Assert.Equal(worldColor, composite.RefractionScene.SourceColor!.ReadPixels());
                marker.Draw(terrain, gbuffer);
                Assert.Equal(-1f, marker.ReadMarker(gbuffer));
                direct.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                Assert.Equal(ErrorCode.NoError, GL.GetError());
                composite.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                Assert.True(composite.RefractionScene.Published);
                Assert.Null(Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
                Assert.Equal(depth, composite.RefractionScene.SourceDepth!.ReadPixels()[0]);
                Assert.Equal(worldColor, composite.RefractionScene.SourceColor!.ReadPixels());
                Assert.NotEqual(sentinel, ordinaryColor.ReadPixels());
            }
        }
        finally
        {
            caller.Stop();
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled = prior;
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionBackgroundScale = priorScale;
        }
    }
    #endregion
}
