using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Runs production surface-cache renderers against controlled engine edges and real GPU resources.</summary>
internal sealed partial class SurfaceCacheRuntimeFixture : IDisposable
{
    private readonly BinaryShaderApiFixture assets = new();
    private readonly ScopedPbrMaterialFixture material = new();
    private readonly ShaderTestFramework drawing = new();
    private readonly WorldPartitionModSystem partitions = new();
    private readonly LumOnDebugRenderer? debug;
    private LumonSceneRelightUpdateRenderer relight = null!;
    private readonly DebugViewController? controller;
    private readonly bool productionOwned;
    private readonly bool enclosure, exposedWall;
    private readonly uint[] feedbackPatches;
    private readonly SpatialLightingScene? spatial;
    private readonly int edge;
    public Block SourceBlock => material.Cube;
    public EngineTerrainBuffers Terrain { get; }
    public RuntimeRenderEvents Events { get; } = new();
    public VgeConfig Config { get; } = new();
    public TraceGeometryRenderer Geometry { get; private set; } = null!;
    public LumonSceneFeedbackUpdateRenderer Feedback { get; private set; } = null!;
    public GBufferManager Buffers { get; }
    public List<RuntimeTraceGeometrySource> Sources { get; } = [];
    public int Draws { get; private set; }
    public int Frames { get; private set; }
    public int BlockLight { get; private set; } = 32;
    public int BlockId => material.Cube.Id;
    public bool GeometryAvailable { get; set; } = true;
    /// <summary>Authors localized source outcomes without bypassing production geometry capture and publication.</summary>
    public System.Func<int,int,int,TraceGeometryVoxel,TraceGeometryVoxel>? TransformVoxel { get; set; }
    /// <summary>Moves the non-spatial fixture camera without changing its authored geometry or feedback pages.</summary>
    public double CameraX { get; set; }
    /// <summary>Selects the chunk whose authored patch identities are submitted by the non-spatial fixture.</summary>
    public VanillaGraphicsExpanded.Numerics.VectorInt3 FeedbackChunk { get; set; } = new(0,1,0);
    /// <summary>Limits authored visible pages so tests can introduce residency gradually without changing geometry.</summary>
    public int VisibleFeedbackPages { get; set; } = int.MaxValue;
    public uint MaterialId { get; private set; }
    public List<string> Logs => assets.Logs;
    public ICoreClientAPI Api { get; }
    public ISurfaceLightingProvider LightingProvider => relight;
    #region Publication observations
    /// <summary>Queries the production publication owner without retaining resources across frames.</summary>
    public bool TryGetLighting(out SurfaceLightingSnapshot snapshot) => relight.TryGetSurfaceLighting(out snapshot);

    /// <summary>Requires every requested room page to have coherent produced lighting before a test freezes the producer budget.</summary>
    public bool AllRequestedLightingReady()
    {
        if (!TryGetLighting(out var snapshot) || !Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _) ||
            !TryGetRequestedPages(mapping, out var pages)) return false;
        using var ready = snapshot.Readiness.MapRange<uint>(0, checked((int)pages.Max(page => page.Physical)+1), MapBufferAccessMask.MapReadBit);
        if (!ready.IsMapped) return false;
        foreach (var page in pages) if (ready.Span[(int)page.Physical] == 0) return false;
        // Admission permits partial lighting. Read every requested texel, coalescing only neighboring requested tiles.
        // Both the snapshot and pooled observations remain local: another call sees writes and resource replacement.
        var regions = SurfaceCacheReadinessReadback.Plan(pages.Select(page => page.Physical),
            snapshot.TileSize, snapshot.TilesPerAxis, snapshot.TilesPerAtlas);
        return SurfaceCacheReadinessReadback.AllInitialized(snapshot.OutgoingRadiance, regions);
    }

    /// <summary>Observes completion of all requested terrain captures without accepting allocation as successful capture.</summary>
    public bool AllRequestedCaptured()
    {
        if (!Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out var mirror) ||
            !TryGetRequestedPages(mapping, out var pages)) return false;
        foreach (var page in pages)
        {
            ulong key = page.Virtual;
            int index = checked((int)LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key) * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk
                + (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key));
            var flags = LumonScenePageTableEntryPacking.UnpackFlags(mirror[index]);
            if ((flags & LumonScenePageTableEntryPacking.Flags.Resident) == 0 ||
                (flags & (LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.Capturing)) != 0) return false;
        }
        return true;
    }

    /// <summary>Resolves every authored request by identity, excluding older resident pages outside the current room.</summary>
    private bool TryGetRequestedPages(IReadOnlyDictionary<uint, ulong> mapping, out List<(uint Physical, ulong Virtual)> pages)
    {
        pages = [];
        var reverse = mapping.ToDictionary(pair => pair.Value, pair => pair.Key);
        var requests = spatial?.Feedback() ?? feedbackPatches.Select(patch =>
            (Chunk: FeedbackChunk, Patch: patch)).ToArray();
        foreach (var request in requests)
        {
            if (!Feedback.TryGetNearChunkSlotAndGeneration(request.Chunk, out uint slot, out _)) return false;
            ulong key = LumonSceneVirtualPageKeyUtil.Pack(slot,
                request.Patch % (uint)LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk);
            if (!reverse.TryGetValue(key, out uint physical)) return false;
            pages.Add((physical, key));
        }
        return pages.Count != 0;
    }

    #endregion

    #region Runtime setup
    /// <summary>Mocks camera and terrain raster inputs while retaining renderer registration, cache work and viewer selection.</summary>
    public SurfaceCacheRuntimeFixture(int requestedPages = 1, bool exposedWall = false, bool enclosure = false, SpatialLightingScene? spatial = null, bool productionOwned = false, int feedbackPlaneX = 0, ControlledVoxelWorld? fallbackWorld = null)
    {
        this.spatial=spatial; edge=spatial==null?2:4;
        this.productionOwned=productionOwned; this.enclosure=enclosure; this.exposedWall=exposedWall;
        Terrain = new(edge,edge);
        feedbackPatches = enclosure
            ? Enumerable.Range(0,6).SelectMany(axis => Enumerable.Range(0,4).Select(tile =>
                1u + 6u * (uint)((axis%2==0?0:7)*64 + (tile/2)*8 + tile%2) + (uint)axis)).ToArray()
            : Enumerable.Range(0,requestedPages).Select(index=>1u+6u*(uint)(feedbackPlaneX * 64 + index)).ToArray();
        material.SetReadiness(true, true, spatial?.SourceAlbedo ?? new System.Numerics.Vector3(spatial?.Reflectance ?? 1), (spatial?.Emission ?? 0)/32f);
        Config.LumOn.Enabled = true;
        var cfg = Config.LumOn.LumonScene;
        cfg.Enabled = true; cfg.NearRadiusChunks = cfg.NearRadiusYChunks = cfg.FarRadiusChunks = cfg.FarRadiusYChunks = 0;
        cfg.MaxAtlasCount = 1; cfg.NearPagesPerChunkBudget = requestedPages; cfg.FarPagesPerChunkBudget = 1;
        cfg.NearTexelsPerVoxelFaceEdge = 1;
        cfg.TraceScene.ClipmapResolution = 32;
        cfg.RelightSeedPagesPerFrame = cfg.RelightDirectPagesPerFrame = cfg.RelightIndirectPagesPerFrame = 1; cfg.RelightTexelsPerPagePerFrame = 64; cfg.RelightRaysPerTexel = 1; cfg.RelightMaxDdaSteps = 256;
        LumOnCameraState? Camera() => spatial?.Camera ?? new LumOnCameraState(CameraX, 32, 0, CameraX, 32, 0, 0);
        if(spatial!=null) { cfg.NearRadiusChunks=cfg.FarRadiusChunks=2; cfg.NearPagesPerChunkBudget=48; }
        var world = new Mock<IClientWorldAccessor>(MockBehavior.Strict);
        world.SetupGet(api => api.Player).Returns((IClientPlayer)null!);
        world.SetupGet(api => api.MapSizeY).Returns(fallbackWorld?.MapSizeY ?? 256);
        if (fallbackWorld != null)
            world.SetupGet(api => api.BlockAccessor).Returns(ControlledBlockAccessor.Create(fallbackWorld));
        world.SetupGet(api => api.Calendar).Returns((IClientGameCalendar)null!);
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        Terrain.Depth.UploadDataImmediate(Enumerable.Repeat(.5f,edge*edge).ToArray());
        var framebuffers = Enumerable.Repeat<FrameBufferRef>(null!, Enum.GetValues<EnumFrameBuffer>().Max(v => (int)v) + 1).ToList();
        framebuffers[(int)EnumFrameBuffer.Primary] = Terrain.Primary;
        var render = RuntimeEngineServices.Render(edge,framebuffers,() => spatial?.View() ?? identity,() => identity,() => Draw());
        var shaders = new Mock<IShaderAPI>(MockBehavior.Strict);
        shaders.Setup(service => service.NewShader(It.IsAny<EnumShaderType>())).Returns(() => new Vintagestory.Client.NoObf.Shader());
        shaders.Setup(service => service.RegisterMemoryShaderProgram(It.IsAny<string>(), It.IsAny<IShaderProgram>())).Returns(1);
        var api = RuntimeEngineServices.Client(assets.Api,Events.Api,world.Object,render,shaders.Object);
        Api = api;
        partitions.StartClientSide(api);
        Buffers = new(api);
        Assert.True(Buffers.EnsureBuffers(edge, edge));
        if (productionOwned) return;
        LumOnDebugShaderProgramFamily.Register(api);
        Geometry = new(api, Config, partitions, CreateSource, Camera);
        Feedback = new(api, Config, Buffers, partitions.GetCoordinator(), Camera);
        Feedback.SetTraceGeometryRenderer(Geometry);
        relight = new(api, Config, Feedback, Geometry);
        debug = new(api, Config, null, Buffers, null, null, Camera);
        debug.SetLumonSceneFeedbackUpdateRenderer(Feedback);
        debug.SetTraceGeometryRenderer(Geometry);
        VgeBuiltInDebugViews.RegisterAll(api, Buffers);
        controller = new(DebugViewRegistry.Instance);
        controller.Initialize(new(api, Config));
        Assert.True(controller.TryActivate("vge.lumon.surfaceCache", out string? error), error);
    }
    /// <summary>Exposes the already initialized engine partition mod to production dependency lookup.</summary>
    internal WorldPartitionModSystem Partitions => partitions;

    /// <summary>Supplies controlled engine geometry while retaining the production source cache and partition scheduler.</summary>
    internal ITraceGeometrySource CreateSource(TraceGeometryMaterials materials)
    {
        uint id = materials.Resolve(material.Cube);
        MaterialId = id;
        var source = new RuntimeTraceGeometrySource((x, y, z) =>
        {
            var voxel = new TraceGeometryVoxel(
            (spatial?.Solid(x,y,z) ?? (enclosure ? x<=0 || x>=7 || y<=32 || y>=39 || z<=0 || z>=7 : !exposedWall || x<=0)) ? 2u | id << 2 : 1u,
            LumonSceneOccupancyPacking.PackClamped(spatial?.Light(x,y,z) ?? BlockLight, 0, 0, (int)id), 0);
            return TransformVoxel?.Invoke(x,y,z,voxel) ?? voxel;
        },
            () => GeometryAvailable, key => spatial?.Loaded(key) ?? true);
        Sources.Add(source);
        return source;
    }

    /// <summary>Observes mod-created owners; this does not change production wiring or registration.</summary>
    internal void AttachProduction(TraceGeometryRenderer geometry, LumonSceneFeedbackUpdateRenderer feedback, LumonSceneRelightUpdateRenderer lighting)
    {
        Geometry = geometry; Feedback = feedback; relight = lighting;
    }

    #endregion

    #region Engine frames and observations
    /// <summary>Runs real registered stage callbacks and supplies the terrain patch raster at the engine boundary.</summary>
    public void Frame()
    {
        TestUniformRing.BeginFrame();
        Events.Render(EnumRenderStage.Opaque);
        if(spatial!=null)
        {
            var patches=spatial.Feedback(); var pixels=new uint[edge*edge*4];
            for(int i=0;i<edge*edge;i++)
            {
                var patch=patches[(Frames*edge*edge+i)%patches.Length];
                if(!Feedback.TryGetNearChunkSlotAndGeneration(patch.Chunk,out uint s,out ushort g)) continue;
                pixels[i*4]=s; pixels[i*4+1]=patch.Patch; pixels[i*4+3]=g;
            }
            Terrain.UploadFeedback(Buffers,pixels);
        }
        else if (Feedback.TryGetNearChunkSlotAndGeneration(FeedbackChunk, out uint slot, out ushort generation))
        {
            int visiblePages = Math.Clamp(VisibleFeedbackPages, 1, feedbackPatches.Length);
            uint[] pixels = Enumerable.Range(0, 4).SelectMany(index => new uint[] { slot, feedbackPatches[(Frames*4+index)%visiblePages], 0, generation }).ToArray();
            Terrain.UploadFeedback(Buffers,pixels);
        }
        Events.Render(EnumRenderStage.Done);
        Terrain.Output.BindWithViewport();
        Events.Render(EnumRenderStage.AfterBlit);
        Frames++;
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Runs the registered geometry callback until the controlled surface domain is published.</summary>
    public void PrimeGeometry(int maximumFrames = 80)
    {
        for (int frame = 0; frame < maximumFrames; frame++)
        {
            TestUniformRing.BeginFrame();
            Events.Render(EnumRenderStage.Opaque);
            if (Geometry.Resources is { TablesRevision: 1, Revision: >= 8 }) return;
            System.Threading.Thread.Yield();
        }
        Assert.Fail($"Controlled geometry did not publish within {maximumFrames} opaque callbacks.");
    }

    /// <summary>Advances bounded production frames until the requested observable state is reached.</summary>
    public void RunUntil(System.Func<bool> condition, int maximumFrames = 80)
    {
        bool complete = condition();
        for (int frame = 0; frame < maximumFrames && !complete; frame++)
        {
            Frame();
            complete = condition();
        }
        if (complete) return;
        // Self-check text and history enumeration serve failure diagnostics only.
        Feedback.TryGetSelfCheckLine(out string feedback);
        relight.TryGetSelfCheckLine(out string relightState);
        string sources = string.Join(",", Sources.Select(source => $"captures={source.CaptureCount},disposed={source.Disposed}"));
        Assert.Fail($"Runtime condition did not settle within {maximumFrames} frames. Feedback: {feedback}. Relight: {relightState}. Sources: {sources}. Geometry: revision={Geometry.Resources?.Revision}, invalidation={Geometry.Resources?.InvalidationRevision}. Executed: {string.Join(", ", Events.Executed.TakeLast(16))}. Logs: {string.Join(" | ", Logs)}");
    }

    /// <summary>Returns whether registered capture and relight callbacks produced a sampleable resident page.</summary>
    public bool SurfaceCacheSettled()
    {
        if (!Feedback.TryGetNearDispatchState(out var pool, out _, out _, out var mirror) ||
            pool.GpuResources?.IrradianceAtlas is not { IsValid: true })
            return false;

        foreach (LumonScenePageTableEntry entry in mirror)
        {
            var flags = LumonScenePageTableEntryPacking.UnpackFlags(entry);
            var pending = LumonScenePageTableEntryPacking.Flags.NeedsCapture |
                LumonScenePageTableEntryPacking.Flags.Capturing |
                LumonScenePageTableEntryPacking.Flags.NeedsRelight |
                LumonScenePageTableEntryPacking.Flags.Relighting;
            if (LumonScenePageTableEntryPacking.UnpackPhysicalPageId(entry) != 0 &&
                (flags & LumonScenePageTableEntryPacking.Flags.Resident) != 0 && (flags & pending) == 0)
                return true;
        }
        return false;
    }

    /// <summary>Returns the currently published physical irradiance atlas identity.</summary>
    public int IrradianceAtlasId()
        => Feedback.TryGetNearDebugSamplingState(out _, out _, out var irradiance, out _, out _, out _)
            ? irradiance.TextureId : 0;

    /// <summary>Returns the current atlas object so recreation is measured independently of recycled GL names.</summary>
    public GpuTexture? IrradianceAtlas()
        => Feedback.TryGetNearDebugSamplingState(out _, out _, out var irradiance, out _, out _, out _)
            ? irradiance : null;

    /// <summary>Returns true once the surface-cache viewer draws lit cache values instead of a diagnostic color.</summary>
    public bool ViewerShowsLighting()
    {
        float[] pixels = ReadPixels();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < .85f || pixels[i + 1] < .85f || pixels[i + 2] < .85f || pixels[i + 3] < .99f) return false;
        }
        return Draws > 0;
    }

    /// <summary>Changes the physical tile layout so the production pool must replace its atlases.</summary>
    public void RequestAtlasRecreation() => Config.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge = Config.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge == 2 ? 1 : 2;

    /// <summary>Changes source lighting and withdraws geometry readiness until the replacement source is published.</summary>
    public void ChangeBlockLight(int value)
    {
        BlockLight=value; if (spatial != null) spatial.BlockLight=value;
        InvalidateGeometry();
    }

    /// <summary>Versions controlled source chunks after a source edit without changing cache mappings or lighting outputs.</summary>
    public void InvalidateGeometry()
    {
        var keys = spatial?.Feedback().Select(p => VanillaGraphicsExpanded.Voxels.ChunkProcessing.ChunkKey.FromChunkCoords(p.Chunk.X,p.Chunk.Y,p.Chunk.Z)).Distinct()
            ?? [VanillaGraphicsExpanded.Voxels.ChunkProcessing.ChunkKey.FromChunkCoords(0,1,0)];
        foreach (var source in Sources.Where(s=>!s.Disposed)) foreach (var key in keys) source.MarkDirty(key);
    }

    /// <summary>Raises the real world-lifetime event consumed by every production owner.</summary>
    public void LeaveWorld() => Events.LeaveWorld();

    /// <summary>Reads the rendered viewer output without inspecting shader internals.</summary>
    public float[] ReadPixels()
    {
        Terrain.Output.BindWithViewport();
        float[] result = new float[edge*edge*4];
        GL.ReadPixels(0, 0, edge, edge, PixelFormat.Rgba, PixelType.Float, result);
        return result;
    }

    /// <summary>Implements the engine mesh draw using a real fullscreen GPU triangle.</summary>
    private void Draw() { Draws++; drawing.RenderQuad(GL.GetInteger(GetPName.CurrentProgram)); }
    #endregion

    #region Teardown
    /// <summary>Disposes production owners before their mocked engine and material dependencies.</summary>
    public void Dispose()
    {
        controller?.Dispose(); debug?.Dispose();
        if (!productionOwned) { relight.Dispose(); Feedback.Dispose(); Geometry.Dispose(); }
        partitions.Dispose();
        if (!productionOwned)
        {
            LumOnDebugShaderProgramFamily.Dispose(Api);
            VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Dispose(Api);
        }
        Buffers.Dispose(); Terrain.Dispose(); drawing.Dispose(); assets.Dispose(); material.Dispose();
    }

    #endregion
}
