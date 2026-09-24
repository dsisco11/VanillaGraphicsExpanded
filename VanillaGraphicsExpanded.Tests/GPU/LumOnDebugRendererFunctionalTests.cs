using Moq;
using VanillaGraphicsExpanded.DebugView;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises geometry and world-probe diagnostics through the registered production renderer.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnDebugRendererFunctionalTests : LumOnShaderFunctionalTestBase
{
    #region Construction
    /// <summary>Uses the shared mandatory GPU context.</summary>
    public LumOnDebugRendererFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }
    #endregion

    #region Renderer dependencies
    /// <summary>Geometry diagnostics must draw while unrelated world-probe runtime data is unavailable.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void GeometryView_DrawsWithoutWorldProbeRuntimeParameters(bool lightingResources, bool publishedGeometry)
        => RenderViews(lightingResources, publishedGeometry);

    /// <summary>World-probe views must settle on one shader variant across absent and published runtime data.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorldProbeView_DrawsAndSwitchesFromGeometry(bool runtimeParameters)
        => RenderViews(true, false, runtimeParameters);

    /// <summary>Runs actual callback frames against controlled GPU resources and optional world-probe placement.</summary>
    private void RenderViews(bool lightingResources, bool publishedGeometry, bool? worldProbeRuntime = null)
    {
        EnsureShaderTestAvailable();
        using var engine = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var terrain = new EngineTerrainBuffers(2, 2);
        using var drawing = new ShaderTestFramework();
        var events = new RuntimeRenderEvents();
        var config = new VgeConfig();
        config.LumOn.Enabled = true;
        config.LumOn.DebugMode = LumOnDebugMode.NearFieldGeometry;
        config.WorldProbeClipmap.ClipmapResolution = 4;
        config.WorldProbeClipmap.ClipmapLevels = 1;
        var world = new Mock<IClientWorldAccessor>();
        float[] view = [1,0,0,0, 0,1,0,0, 0,0,1,0, -.5f,-.5f,0,1];
        float[] inverseProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        inverseProjection[0] *= .01f; inverseProjection[5] *= .01f;
        var projection = new float[16];
        MatrixHelper.Invert(inverseProjection, projection);
        var framebuffers = Enumerable.Repeat<FrameBufferRef>(null!, Enum.GetValues<EnumFrameBuffer>().Max(v => (int)v) + 1).ToList();
        framebuffers[(int)EnumFrameBuffer.Primary] = terrain.Primary;
        int draws = 0;
        var programs = new Dictionary<string, IShaderProgram>();
        var shaderApi = new Mock<IShaderAPI>();
        shaderApi.Setup(api => api.NewShader(It.IsAny<EnumShaderType>())).Returns(() => new Vintagestory.Client.NoObf.Shader());
        shaderApi.Setup(api => api.GetProgramByName(It.IsAny<string>())).Returns((string name) => programs.GetValueOrDefault(name)!);
        shaderApi.Setup(api => api.RegisterMemoryShaderProgram(It.IsAny<string>(), It.IsAny<IShaderProgram>()))
            .Callback((string name, IShaderProgram program) => programs[name] = program);
        var render = RuntimeEngineServices.Render(2, framebuffers, () => view, () => projection, () =>
        {
            draws++;
            drawing.RenderQuad(programs["lumon_debug_worldprobe"].ProgramId);
        });
        var api = RuntimeEngineServices.Client(assets.Api, events.Api, world.Object, render, shaderApi.Object);
        using var buffers = new LumOnBufferManager(api, config);
        buffers.EnsureBuffers(2, 2);
        Assert.True(buffers.IsInitialized);
        using var gbuffer = new GBufferManager(api);
        using var probes = new LumOnWorldProbeClipmapBufferManager(api, config);
        probes.EnsureResources();
        if (worldProbeRuntime == true)
        {
            probes.UpdateRuntimeParams(new Vec3d(), default, config.WorldProbeClipmap.ClipmapBaseSpacing,
                1, 4, [new System.Numerics.Vector3(-2)], [default]);
        }
        using var geometry = new NearFieldVoxelFixture();
        if (publishedGeometry)
        {
            var voxelWorld = new ControlledVoxelWorld();
            var block = new Block { BlockId = 1, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
            for (int y = -8; y <= 8; y++)
            for (int x = -8; x <= 8; x++) voxelWorld.SetBlock(x, y, -5, block);
            geometry.Publish(voxelWorld);
        }
        using var renderer = new LumOnDebugRenderer(api, config, lightingResources ? buffers : null,
            lightingResources ? gbuffer : null, null, lightingResources ? probes : null,
            () => new LumOnCameraState(0, 0, 0, 0, 0, 0, 0));
        if (publishedGeometry) renderer.SetNearFieldSceneProvider(new GeometryProvider(geometry.Scene.Backend));
        LumOnDebugShaderProgramFamily.Register(api);
        var probeView = VgeBuiltInDebugViews.ProbesDebugViewState.Instance;
        bool previousHeatmap = probeView.GetImportanceSurfaceHeatmapEnabled();
        probeView.SetImportanceSurfaceHeatmapEnabled(true);
        try
        {
            // Both published geometry and the unavailable-provider diagnostic must render
            // without world-probe scheduling, lighting, or screen-probe outputs.
            for (int frame = 0; frame < 8; frame++)
            {
                TestUniformRing.BeginFrame();
                terrain.Output.BindWithViewport();
                events.Render(EnumRenderStage.AfterBlit);
            }
            Assert.True(draws > 0, string.Join("\n", assets.Logs));
            float[] pixel = new float[4];
            GL.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.Float, pixel);
            if (publishedGeometry)
            {
                Assert.True(pixel[2] > .1f);
                Assert.InRange(pixel[0] / pixel[2], .098f, .102f);
                Assert.InRange(pixel[1] / pixel[2], .848f, .852f);
            }
            else
            {
                Assert.InRange(pixel[0], .099f, .101f);
                Assert.InRange(pixel[1], .199f, .201f);
                Assert.InRange(pixel[2], .799f, .801f);
            }
            if (worldProbeRuntime.HasValue)
            {
                // A mode switch must settle rather than repeatedly queue incompatible layouts.
                foreach (var mode in new[]
                {
                    LumOnDebugMode.WorldProbeIrradianceCombined, LumOnDebugMode.WorldProbeIrradianceLevel,
                    LumOnDebugMode.WorldProbeConfidence, LumOnDebugMode.WorldProbeShortRangeAoDirection,
                    LumOnDebugMode.WorldProbeShortRangeAoConfidence, LumOnDebugMode.WorldProbeHitDistance,
                    LumOnDebugMode.WorldProbeMetaFlagsHeatmap, LumOnDebugMode.WorldProbeBlendWeights,
                    LumOnDebugMode.WorldProbeCrossLevelBlend, LumOnDebugMode.WorldProbeRawConfidences,
                    LumOnDebugMode.WorldProbeLightingEffect, LumOnDebugMode.WorldProbeSuppressedLighting,
                    LumOnDebugMode.WorldProbeImportance, LumOnDebugMode.NearFieldGeometry
                })
                {
                    config.LumOn.DebugMode = mode;
                    int before = draws;
                    for (int frame = 0; frame < 8; frame++)
                    {
                        TestUniformRing.BeginFrame();
                        terrain.Output.BindWithViewport();
                        events.Render(EnumRenderStage.AfterBlit);
                    }
                    Assert.True(draws >= before + 2, $"{mode} produced {draws - before} draws across eight callbacks.\n{string.Join("\n", assets.Logs)}");
                    Assert.Empty(events.MainThreadTasks);
                }
            }
        }
        finally
        {
            probeView.SetImportanceSurfaceHeatmapEnabled(previousHeatmap);
            foreach (var program in programs.Values) program.Dispose();
        }
    }
    #endregion

    #region Controlled geometry
    /// <summary>Exposes the actual published geometry to the production callback.</summary>
    private sealed class GeometryProvider(TraceGeometryGpuScene scene) : ITraceGeometrySceneProvider
    {
        /// <summary>Returns the scene whose readiness and voxel textures the fixture published.</summary>
        public TraceGeometryGpuScene PrepareScene() => scene;
    }
    #endregion
}
