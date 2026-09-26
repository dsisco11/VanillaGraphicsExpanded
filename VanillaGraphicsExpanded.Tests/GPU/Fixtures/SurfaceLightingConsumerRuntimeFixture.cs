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
    private static readonly System.Reflection.FieldInfo surfaceQueriesField =
        typeof(LumOnWorldProbeUpdateRenderer).GetField("surfaceQueries",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The runtime fixture could not locate the renderer's query owner.");
    private readonly EngineShaderPlatformScope platform = new();
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
    public SurfaceLightingConsumerRuntimeFixture(bool sh9, SpatialLightingScene? spatial = null, bool pbrComposition = false, bool shortProbeRange = false)
    {
        this.spatial=spatial; edge=spatial==null?2:4;
        Cache = new(requestedPages:24,enclosure:true,spatial:spatial,productionOwned:true);
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
            EngineUniforms.SunPosition3D = new Vintagestory.API.MathTools.Vec3f(0, 0, -1);
        }
        world.SetupGet(api => api.Player).Returns((IClientPlayer)null!);
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
            materials[index + 2] = receiver.Emission; materials[index + 3] = receiver.Reflectivity;
        }
        Cache.Terrain.UploadTerrain(Cache.Buffers,
            raster?.Depth ?? Enumerable.Repeat(depth,edge*edge).ToArray(),
            raster?.Normal ?? Enumerable.Range(0,edge*edge).SelectMany(_=>new[]{.5f,.5f,1f,0f}).ToArray(),
            materials, colors);
        Cache.Frame();
        if(Screen.IndirectFullTex!=null) Energy(FinalPixels());
        if(WorldBuffers.Resources!=null) Energy(WorldPixels());
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

    /// <summary>Reads the final full-resolution indirect output that the renderer publishes to composition.</summary>
    public float[] FinalPixels() => Screen.IndirectFullTex?.ReadPixels() ?? [];

    /// <summary>Reads the primary engine target after registered PBR composition.</summary>
    public float[] ComposedPixels() => Cache.Terrain.Color.ReadPixels();

    /// <summary>Reads the actual worker/query/upload-owned world radiance atlas.</summary>
    public float[] WorldPixels() => WorldBuffers.Resources?.ProbeRadianceAtlas.ReadPixels() ?? [];

    /// <summary>Finds published confidence across physical slots; the interior probe need not occupy slot zero.</summary>
    public float WorldConfidence => WorldBuffers.Resources?.ProbeMeta0.ReadPixels().Where((_, index) => (index & 3) == 0).Max() ?? 0;

    /// <summary>Checks every RGB channel and returns the peak finite energy at an observable boundary.</summary>
    public static float Energy(float[] pixels)
    {
        Assert.NotEmpty(pixels); float peak=0;
        for(int i=0;i<pixels.Length;i++)
        {
            Assert.True(float.IsFinite(pixels[i]));
            if(i%4!=3) peak=Math.Max(peak,Math.Abs(pixels[i]));
        }
        return peak;
    }

    /// <summary>Submits the currently bound production shader through a real GPU fullscreen primitive.</summary>
    private void Draw()
    {
        int program=GL.GetInteger(GetPName.CurrentProgram); Assert.NotEqual(0,program);
        DrawnPrograms.Add(program); drawing.RenderQuad(program);
        // Engine RenderMesh preserves the active shader across repeated draws (including HZB mip levels).
        GL.UseProgram(program);
    }
    #endregion

    #region Disposal
    /// <summary>Unblocks workers before retiring consumers, then their producer and engine dependencies.</summary>
    public void Dispose()
    {
        World.ReleaseWorker(); host.Dispose();
        programs.Dispose(); drawing.Dispose(); World.Dispose(); Cache.Dispose(); platform.Dispose();
    }
    #endregion
}
