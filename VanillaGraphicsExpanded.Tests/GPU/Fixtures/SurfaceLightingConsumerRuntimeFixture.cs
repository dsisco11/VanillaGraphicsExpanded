using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Composes registered cache, screen-probe and worker-driven world-probe consumers against controlled engine inputs.</summary>
internal sealed partial class SurfaceLightingConsumerRuntimeFixture : IDisposable
{
    private readonly VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservations observations;
    private static readonly System.Reflection.FieldInfo surfaceQueriesField =
        typeof(LumOnWorldProbeUpdateRenderer).GetField("surfaceQueries",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The runtime fixture could not locate the renderer's query owner.");
    private readonly EngineShaderPlatformScope platform = new();
    private readonly HarmonyLib.Harmony shaderStop = new("tests.surface-lighting-stop." + Guid.NewGuid());
    private readonly ShaderTestFramework drawing = new();
    private readonly RuntimeLightingPrograms programs = new();
    private readonly VanillaGraphicsExpanded.ModSystems.WorldProbeModSystem worldSystem;
    private readonly RuntimeLightingHost host;
    private readonly SpatialLightingScene? spatial;
    private readonly int edge;
    private readonly float[] projection = LumOnTestInputFactory.CreateRealisticProjection();
    public SurfaceCacheRuntimeFixture Cache { get; }
    public RuntimeProbeWorld World { get; }
    /// <summary>Models an unavailable engine world accessor while retaining existing GPU resources.</summary>
    public bool WorldAccessorAvailable { get; set; }=true;
    public LumOnBufferManager Screen { get; }
    public LumOnWorldProbeClipmapBufferManager WorldBuffers { get; }
    public LumOnWorldProbeUpdateRenderer WorldRenderer { get; }
    public VanillaGraphicsExpanded.PBR.DirectLightingBufferManager Direct => host.Direct;
    public DefaultShaderUniforms EngineUniforms { get; } = new() { ZNear = .1f, ZFar = 100 };
    public List<int> DrawnPrograms { get; } = [];
    public IReadOnlyCollection<string> LoadedPrograms => programs.Loaded;
    public const int FrameBudget = 160;
    /// <summary>Exposes the produced image under the runtime owner's existing consumption interval.</summary>
    internal GpuTexture FinalImage => Screen.IndirectFullTex ?? throw new InvalidOperationException("Indirect output is not allocated.");
    /// <summary>Exposes all directional radiance texels without downloading their contents.</summary>
    internal GpuTexture WorldImage => WorldBuffers.Resources?.ProbeRadianceAtlas ?? throw new InvalidOperationException("World output is not allocated.");
    /// <summary>Exposes the scene-linear RGB or RGBA target without synthesizing a CPU alpha channel.</summary>
    internal GpuTexture SceneLinearImage => host.SceneLinearColor ?? throw new InvalidOperationException("PBR composition has not produced scene-linear output.");
    /// <summary>Exposes the engine primary output under the runtime fixture's ownership interval.</summary>
    internal GpuTexture ComposedImage => Cache.Terrain.Color;

    /// <summary>Checks every component for finiteness and returns peak RGB magnitude with a bounded GPU reduction.</summary>
    internal float Energy(GpuTexture texture, string? observation = null)
    {
        var all = Cache.ImageAssertions.Statistics.Inspect(texture);
        Assert.True(all.NonFinite == 0, $"{observation ?? "Lighting output"}: {all}");
        var rgb = Cache.ImageAssertions.Statistics.Inspect(texture, channelMask: 7);
        return (float)Math.Max(Math.Abs(rgb.Minimum), Math.Abs(rgb.Maximum));
    }
    /// <summary>Supplies authored receiver pixels before any registered lighting callback runs.</summary>
    public System.Func<int, int, RuntimeReceiverSurface>? Receiver { get; set; }

    /// <summary>Observes in-flight queries from the test boundary without adding a production inspection API or polling the fence.</summary>
    public bool HasPendingSurfaceLightingQueries =>
        (surfaceQueriesField.GetValue(WorldRenderer) as VanillaGraphicsExpanded.LumOn.Scene.SurfaceLightingQueryBatch)?.Pending == true;

    /// <summary>Observes the actual compute fence without draining results or exposing a production diagnostic API.</summary>
    public bool HasPendingGpuTrace
    {
        get
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var router=typeof(LumOnWorldProbeUpdateRenderer).GetField("traceService",flags)!.GetValue(WorldRenderer);
            if(router==null)return false;
            var backend=router.GetType().GetField("gpu",flags)!.GetValue(router);
            return (typeof(LumOnWorldProbeGpuTraceBackend).GetField("batch",flags)!.GetValue(backend) as WorldProbeTraceBatch)?.Pending==true;
        }
    }

    #region Composition
    /// <summary>Registers real consumers with the producer's engine events and injects real publication providers.</summary>
    public SurfaceLightingConsumerRuntimeFixture(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservations observations, bool sh9, SpatialLightingScene? spatial = null, bool pbrComposition = false, bool shortProbeRange = false)
    {
        this.observations = observations;
        observations.Programs.Prepare("inspection", 0, 0);
        // The engine Stop method is sealed; run its production ownership hook alongside native geometry submission.
        this.spatial=spatial; edge=spatial==null?2:4;
        Cache = new(observations, requestedPages:24,enclosure:true,spatial:spatial,productionOwned:true);
        World = new(Cache.SourceBlock,spatial);
        var cfg=Cache.Config.LumOn;
        cfg.ProbeSpacingPx=1; cfg.ProbeAtlasTexelsPerFrame=8; cfg.RayMaxDistance=16;
        cfg.AnchorJitterEnabled=false; cfg.EnableProbePIS=false; cfg.EnableReprojectionVelocity=false;
        cfg.Intensity=1; cfg.IndirectTint=[1,1,1]; cfg.LumonScene.RelightSeedPagesPerFrame = cfg.LumonScene.RelightDirectPagesPerFrame = cfg.LumonScene.RelightIndirectPagesPerFrame = 4;
        cfg.ProbeAtlasGather=sh9?VgeConfig.ProbeAtlasGatherMode.EvaluateProjectedSH:VgeConfig.ProbeAtlasGatherMode.IntegrateAtlas;
        var wp=Cache.Config.WorldProbeClipmap;
        // Production trace reach is spacing times resolution; 16 blocks cover every room diagonal.
        // The short-range regression deliberately leaves that reach insufficient to resolve all directions.
        wp.ClipmapResolution=shortProbeRange?1:4; wp.ClipmapLevels=1; wp.ClipmapBaseSpacing=4;
        wp.OctahedralTileSize=8; wp.AtlasTexelsPerUpdate=64; wp.TraceMaxProbesPerFrame=1;
        wp.PerLevelProbeUpdateBudget=[1]; wp.UploadBudgetBytesPerFrame=2096; wp.EnableDirectionPIS=false;
        /// <summary>Supplies an engine camera positioned inside the controlled enclosure.</summary>
        LumOnCameraState? Camera() => spatial?.Camera ?? new LumOnCameraState(4,36,6,4,36,6,0);
        if(spatial!=null)
        {
            wp.ClipmapResolution=shortProbeRange?2:8; wp.ClipmapBaseSpacing=2; wp.TraceMaxProbesPerFrame=8;
            wp.PerLevelProbeUpdateBudget=[8]; wp.UploadBudgetBytesPerFrame=8*2096;
            cfg.ProbeAtlasTexelsPerFrame=64; cfg.TemporalAlpha=0;
        }
        var world = new Mock<IClientWorldAccessor>(MockBehavior.Strict);
        if (pbrComposition)
        {
            Assert.Null(VanillaGraphicsExpanded.PBR.Postprocessing.AmbientOcclusionRenderer.Texture);
            EngineUniforms.SunPosition3D = new Vintagestory.API.MathTools.Vec3f(0, 0, -1);
        }
        world.SetupGet(api => api.Player).Returns(RuntimeEngineServices.CameraPlayer(Camera));
        world.SetupGet(api => api.ElapsedMilliseconds).Returns(() => Cache.Frames * 16L);
        world.SetupGet(api => api.BlockAccessor).Returns(()=>WorldAccessorAvailable ? World.Accessor : null!);
        world.SetupGet(api => api.Calendar).Returns((IClientGameCalendar)null!);
        world.SetupGet(api => api.MapSizeY).Returns(256);
        var render = RuntimeEngineServices.Render(edge,Cache.Api.Render.FrameBuffers,
            () => spatial?.View() ?? Cache.Api.Render.CameraMatrixOriginf,() => projection,() => Draw(), EngineUniforms);
        worldSystem = new(() => Cache.Config, _ => Camera());
        var mods = new Mock<IModLoader>(MockBehavior.Strict);
        mods.Setup(api => api.GetModSystem<VanillaGraphicsExpanded.ModSystems.WorldProbeModSystem>(It.IsAny<bool>())).Returns(worldSystem);
        mods.Setup(api => api.GetModSystem<VanillaGraphicsExpanded.ModSystems.WorldPartitionModSystem>(It.IsAny<bool>())).Returns(Cache.Partitions);
        var api = RuntimeEngineServices.Client(Cache.Api,Cache.Events.Api,world.Object,render,programs.Api,mods.Object,RuntimeEngineServices.Input());
        programs.Initialize(api);
        host=new(api,Cache,worldSystem,Camera,pbrComposition);
        WorldRenderer=host.WorldRenderer;
        Screen=host.Screen;
        WorldBuffers=worldSystem.GetClipmapBufferManagerOrNull()!;
    }
    #endregion

    #region Frames and observations
    /// <summary>Supplies terrain raster data, then invokes the registered engine stages without calling individual lighting passes.</summary>
    public void Frame()
    {
        float z=-5,depth=(projection[10]*z+projection[14])/(projection[11]*z+projection[15])*.5f+.5f;
        var raster=spatial?.Raster(edge,projection);
        var materials = new float[edge * edge * 4];
        var colors = new float[edge * edge * 4];
        for (int y = 0; y < edge; y++) for (int x = 0; x < edge; x++)
        {
            var receiver = Receiver?.Invoke(x, y) ?? new RuntimeReceiverSurface(new(.5f, .5f, .5f));
            int index = (y * edge + x) * 4;
            colors[index] = receiver.Albedo.X; colors[index + 1] = receiver.Albedo.Y; colors[index + 2] = receiver.Albedo.Z; colors[index + 3] = .5f;
            materials[index] = receiver.Roughness; materials[index + 1] = receiver.Metallic;
            materials[index + 2] = receiver.Emission; materials[index + 3] = receiver.Transmission;
        }
        Cache.Terrain.UploadTerrain(Cache.Buffers,
            raster?.Depth ?? Enumerable.Repeat(depth,edge*edge).ToArray(),
            raster?.Normal ?? Enumerable.Range(0,edge*edge).SelectMany(_=>new[]{.5f,.5f,1f,0f}).ToArray(),
            materials, colors);
        host.CameraWorld.RefreshCamera();
        using (var submissions = new NativeIndexedDrawCapture(() =>
        {
            int program = GL.GetInteger(GetPName.CurrentProgram);
            Assert.NotEqual(0, program);
            DrawnPrograms.Add(program);
        })) Cache.Frame();
        // Storage allocation precedes the first submitted frame; only published indirect output is readable.
        if(Screen.HasPublishedIndirect) Energy(FinalImage, "final indirect output");
        if(WorldBuffers.Resources!=null) Energy(WorldImage, "world radiance atlas");
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Advances bounded production frames and observes external completion without arbitrary scheduling delays.</summary>
    public void RunUntil(Func<bool> condition,int maximumFrames=FrameBudget)
    {
        var remainingWait = TimeSpan.FromSeconds(10);
        bool complete = condition();
        for(int i=0;i<maximumFrames&&!complete;i++)
        {
            Frame();
            complete = condition();
            if (complete || i + 1 == maximumFrames) break;
            // Bound cumulative external waiting independently of the existing simulated-frame budget.
            if (remainingWait <= TimeSpan.Zero) throw new TimeoutException("Lighting external-wait budget exhausted.");
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            RuntimeLightingCompletionWait.Wait(WorldRenderer, World, remainingWait);
            remainingWait -= elapsed.Elapsed;
            // Only the deliberate worker milestone can satisfy these predicates outside a render frame.
            if (World.WorkerHeld) complete = condition();
        }
        var observations = ObserveRequiredWaitOutputs();
        if (!complete) Assert.Fail(DescribeWaitFailure(maximumFrames, observations));
    }

    /// <summary>Finds published confidence across physical slots; the interior probe need not occupy slot zero.</summary>
    public float WorldConfidence => WorldBuffers.Resources is { } resources
        // The former packed RG scan selected scalar indices divisible by four: red from even physical slots.
        ? (float)Cache.ImageAssertions.Statistics.Inspect(resources.ProbeMeta0, channelMask: 1, pixelStride: 2).Maximum : 0;

    /// <summary>Submits the currently bound production shader through a real GPU fullscreen primitive.</summary>
    private void Draw()
    {
        int program=GL.GetInteger(GetPName.CurrentProgram); Assert.NotEqual(0,program);
        drawing.RenderGeometry();
        // Engine RenderMesh preserves the active shader across repeated draws (including HZB mip levels).

    }
    #endregion

    #region Disposal
    /// <summary>Unblocks workers before retiring consumers, then their producer and engine dependencies.</summary>
    public void Dispose()
    {
        World.ReleaseWorker(); host.Dispose();
        programs.Dispose(); drawing.Dispose(); World.Dispose(); Cache.Dispose(); platform.Dispose();
        shaderStop.UnpatchAll(shaderStop.Id);
    }
    #endregion
}
