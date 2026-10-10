using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Runs particle capture publication and retirement through registered renderer callbacks.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorParticlePublicationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Only successful current-frame captures publish corrected depth and retire on every owner boundary.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegisteredCapturePublishesAndInvalidatesAcrossLifecycle(bool ssao)
    {
        EnsureContextValid();
        // Direct owning attachments require a fresh disposal lifetime after earlier GPU tests retire it.
        fixture.InitializeResourceDisposal();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var programs = new RuntimeLightingPrograms();
        using var drawing = new ShaderTestFramework();
        using var material = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        using var glow = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba8);
        using var depthStorage = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent32f);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var normal = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        using var position = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        using var engine = GpuFramebuffer.Create(ssao ? [material, glow, normal, position] : [material, glow], depth);
        var primary = new FrameBufferRef
        {
            FboId = engine.FboId,
            Width = 2,
            Height = 2,
            DepthTextureId = depth.TextureId,
            ColorTextureIds = ssao ? [material.TextureId, glow.TextureId, normal.TextureId, position.TextureId] : [material.TextureId, glow.TextureId]
        };
        var frames = Enumerable.Repeat<FrameBufferRef>(null!, 25).ToList();
        frames[0] = primary;
        var events = new RuntimeRenderEvents();
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        int resolveDraws = 0;
        var render = RuntimeEngineServices.Render(2, frames, () => identity, () => identity,
            () => { resolveDraws++; drawing.RenderGeometry(); });
        var renderMock = Mock.Get(render);
        renderMock.SetupGet(value => value.CurrentRenderStage).Returns(EnumRenderStage.Opaque);
        renderMock.SetupGet(value => value.CurrentFrameBuffer).Returns(primary);
        var world = new Mock<IClientWorldAccessor> { DefaultValue = DefaultValue.Mock };
        world.SetupGet(value => value.Player).Returns(
            RuntimeEngineServices.CameraPlayer(() => new VanillaGraphicsExpanded.LumOn.LumOnCameraState(0,0,0,0,0,0,0)));
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name switch
        {
            "get_Render" => render,
            "get_World" => world.Object,
            "get_Event" => events.Api,
            "get_Shader" => programs.Api,
            _ => method.Invoke(assets.Api, args)
        });
        programs.Initialize(api);
        using var frameCamera = new VgeFrameRenderer(api);
        using var lights = new VgeLightsRenderer(api);
        using var gbuffer = new GBufferManager(api);
        Assert.True(gbuffer.EnsureBuffers(2, 2));
        using var capture = new SceneColorParticleCapture(api, gbuffer);
        using var hierarchy = new DepthHierarchyPass();
        using var hierarchyCompute = DepthHierarchyComputeShader.Create(api);
        using var hierarchyDraw = new PostprocessDraw();
        using var directBuffers = new DirectLightingBufferManager(api);
        using var direct = new DirectLightingRenderer(api, gbuffer, directBuffers);
        var config = new VanillaGraphicsExpanded.LumOn.VgeConfig(); config.LumOn.Enabled = false;
        using var composite = new PBRCompositeRenderer(api, gbuffer, directBuffers, config, () => null);
        using var separateRead = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba16f);
        bool previousRefraction = VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled;
        int previousScale = VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionBackgroundScale;
        VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled = true;
        VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionBackgroundScale = 1;
        Assert.Equal(8.5, capture.RenderOrder);
        Assert.Contains(events.Registrations, value => ReferenceEquals(value.Renderer, capture) && value.Stage == EnumRenderStage.Before);
        Assert.Contains(events.Registrations, value => ReferenceEquals(value.Renderer, capture) && value.Stage == EnumRenderStage.Opaque);
        var previousCube = ShaderPrograms.Particlescube;
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, "#version 430 core\nvoid main(){gl_Position=vec4(0);}");
        int fragment = shaders.Compile(ShaderType.FragmentShader, "#version 430 core\nuniform int vge_sceneLinear;out vec4 color;void main(){color=vec4(vge_sceneLinear);}");
        using var linked = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        var cube = new LinkedCube { ProgramId = linked.ProgramId };
        cube.Populate();
        try
        {
            ShaderPrograms.Particlescube = null;
            Assert.False(capture.PrepareFrame(true));
            ShaderPrograms.Particlescube = cube;
            // A matching uniform alone cannot establish the compiled color convention.
            Assert.True(cube.HasUniform("vge_sceneLinear"));
            Assert.False(capture.PrepareFrame(true));
            ShaderCapabilities.Publish(cube, ShaderCapability.SceneColorConvention);
            Assert.False(capture.PrepareFrame(false));
            Assert.Null(SceneColorParticleCapture.BeginDraw(1));
            for (int cycle = 0; cycle < 4; cycle++)
            {
                events.Render(EnumRenderStage.Before);
                frameCamera.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                lights.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                Assert.Null(SceneColorParticleCapture.Layer(api));
                Assert.Equal(depth.TextureId, SceneColorParticleCapture.ReceiverDepth(api, depth.TextureId));
                Assert.True(cube.HasUniform("vge_sceneLinear"));
                Assert.True(capture.PrepareFrame(true), string.Join(Environment.NewLine, assets.Logs));
                Assert.Null(SceneColorParticleCapture.BeginDraw(0));
                engine.BindWithViewport();
                GL.DepthMask(true);
                GL.ClearBuffer(ClearBuffer.Depth, 0, new[] { .75f });
                if (ssao)
                {
                    GL.ClearBuffer(ClearBuffer.Color, 2, new[] { .25f, .5f, .75f, 1f });
                    GL.ClearBuffer(ClearBuffer.Color, 3, new[] { 1f, 2f, 3f, 4f });
                }
                ShaderProgramBase.CurrentShaderProgram = cube;
                StateCache.Current.UseProgram(cube.ProgramId);
                ShaderProgramBase.CurrentShaderProgram = null;
                Assert.Null(SceneColorParticleCapture.BeginDraw(1));
                ShaderProgramBase.CurrentShaderProgram = cube;
                renderMock.SetupGet(value => value.CurrentRenderStage).Returns(EnumRenderStage.OIT);
                Assert.Null(SceneColorParticleCapture.BeginDraw(1));
                GL.GetUniform(cube.ProgramId, GL.GetUniformLocation(cube.ProgramId, "vge_sceneLinear"), out int inactiveMode);
                Assert.Equal(0, inactiveMode);
                renderMock.SetupGet(value => value.CurrentRenderStage).Returns(EnumRenderStage.Opaque);
                renderMock.SetupGet(value => value.CurrentFrameBuffer).Returns(new FrameBufferRef { FboId = -1 });
                Assert.Null(SceneColorParticleCapture.BeginDraw(1));
                renderMock.SetupGet(value => value.CurrentFrameBuffer).Returns(primary);
                using (var scope = SceneColorParticleCapture.BeginDraw(1))
                {
                    Assert.NotNull(scope);
                    GL.GetUniform(cube.ProgramId, GL.GetUniformLocation(cube.ProgramId, "vge_sceneLinear"), out int activeMode);
                    Assert.Equal(1, activeMode);
                    // Mimic the original renderer's successful empty submission; no late
                    // geometry is fabricated, and the real resolve still executes on GPU.
                    scope.Complete();
                }
                Assert.Null(SceneColorParticleCapture.Layer(api));
                using (var hostile = new HostileFullscreenState())
                {
                    int beforeRejected = resolveDraws;
                    capture.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                    Assert.Null(SceneColorParticleCapture.Layer(api));
                    Assert.Equal(beforeRejected, resolveDraws);
                    Assert.Same(cube, ShaderProgramBase.CurrentShaderProgram);
                    Assert.Equal(cube.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
                    hostile.AssertRestored();
                    // The installed particle renderer stops its shader before later opaque callbacks.
                    // Model its observed program-zero handoff rather than inventing a foreign resource footprint.
                    cube.Stop(); StateCache.Current.NotifyProgramBound(0);
                    capture.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                    hostile.AssertRestored();
                }
                var layer = SceneColorParticleCapture.Layer(api);
                Assert.NotNull(layer);
                Assert.Null(SceneColorParticleCapture.Layer(assets.Api));
                Assert.Equal(depth.TextureId, SceneColorParticleCapture.ReceiverDepth(assets.Api, depth.TextureId));
                Assert.All(layer.ReadPixels(), value => Assert.Equal(0, value));
                int receiver = SceneColorParticleCapture.ReceiverDepth(api, depth.TextureId);
                Assert.NotEqual(depth.TextureId, receiver);
                // Distinguish corrected material depth from a later primary-depth change.
                // The shared generator must sample the actual published receiver instance.
                var corrected = SceneColorParticleCapture.ReceiverDepthTexture(api)!;
                depthStorage.UploadDataImmediate(new[] { .25f, .25f, .25f, .25f });
                hierarchy.Prepare(2, 2, hierarchyCompute);
                hierarchy.Render(corrected);
                for (int mip = 0; mip < hierarchy.Texture!.MipLevels; mip++)
                    Assert.All(hierarchy.Texture.ReadPixels(mip), value => Assert.Equal(.75f, value));
                int perCycle = ssao ? 6 : 1;
                Assert.Equal(cycle * perCycle + 1, resolveDraws);
                if (ssao) Assert.Equal(new[] { 1f, 2f, 3f, 4f }, engine[3].ReadPixels()[..4]);
                SceneColorParticleCapture.RestoreSsao(assets.Api);
                Assert.Equal(cycle * perCycle + 1, resolveDraws);
                // Model the compositor's later boundary explicitly: receiver separation
                // above must not restore metadata before deferred material lighting.
                if (ssao)
                {
                    // The real independent compositor owns SSAO restoration and receiver
                    // reduction in one boundary, including first-use target preparation.
                    ShaderProgramBase.CurrentShaderProgram = null;
                    StateCache.Current.UnbindProgram();
                    StateCache.Current.Invalidate(EPipelineState.Depth | EPipelineState.Blend | EPipelineState.Viewport);
                    StateCache.Current.BindFramebuffer(FramebufferTarget.DrawFramebuffer, engine.FboId);
                    StateCache.Current.BindFramebuffer(FramebufferTarget.ReadFramebuffer, separateRead.FboId);
                    StateCache.Current.ApplyDynamic(new VanillaGraphicsExpanded.Rendering.Pipeline.State.DynamicDrawState { X = 1, Y = 1, Width = 1, Height = 1 });
                    direct.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                    using (var hostile = new HostileFullscreenState())
                    {
                        composite.OnRenderFrame(.016f, EnumRenderStage.Opaque);
                        hostile.AssertRestored();
                    }
                    Assert.True(composite.RefractionScene.Published);
                    Assert.Equal(2, composite.RefractionScene.BackgroundScale);
                    Assert.Equal(engine.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
                    Assert.Equal(separateRead.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
                    int[] viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport);
                    Assert.Equal(new[] { 1, 1, 1, 1 }, viewport);
                }
                else SceneColorParticleCapture.RestoreSsao(api);
                Assert.Equal((cycle + 1) * perCycle, resolveDraws);
                if (ssao) Assert.Equal(new[] { 1f, 2f, 3f, 4f }, engine[3].ReadPixels()[..4]);
                if (cycle == 0) capture.OnRenderFrame(.016f, EnumRenderStage.Before);
                if (cycle == 1) events.LeaveWorld();
                if (cycle == 2) ScreenResourceManager.HandleScreenResize();
                if (cycle == 3) Assert.False(capture.PrepareFrame(false));
                Assert.Null(SceneColorParticleCapture.Layer(api));
                Assert.Equal(depth.TextureId, SceneColorParticleCapture.ReceiverDepth(api, depth.TextureId));
                if (cycle > 0)
                {
                    GpuResourceManagerSystem.CaptureDisposalQueue().DrainPending();
                    Assert.False(layer.IsValid);
                }
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionEnabled = previousRefraction;
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.WaterRefractionBackgroundScale = previousScale;
            ShaderProgramBase.CurrentShaderProgram = null;
            ShaderCapabilities.Forget(cube);
            ShaderPrograms.Particlescube = previousCube;
            StateCache.Current.UseProgram(0);
        }
        capture.Dispose();
        capture.Dispose();
        Assert.Throws<ObjectDisposedException>(() => capture.PrepareFrame(true));
        Assert.DoesNotContain(events.Registrations, value => ReferenceEquals(value.Renderer, capture));
    }
    #endregion

    #region Private
    /// <summary>Publishes the linked engine cube shader uniform table without creating a game renderer.</summary>
    private sealed class LinkedCube : ShaderProgramParticlescube
    {
        /// <summary>Registers the actual driver location used by the production scene-mode setter.</summary>
        internal void Populate() => uniformLocations["vge_sceneLinear"] = GL.GetUniformLocation(ProgramId, "vge_sceneLinear");
    }
    #endregion
}
