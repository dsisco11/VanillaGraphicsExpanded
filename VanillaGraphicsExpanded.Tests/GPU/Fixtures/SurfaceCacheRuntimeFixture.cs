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
internal sealed class SurfaceCacheRuntimeFixture : IDisposable
{
    private readonly BinaryShaderApiFixture assets = new();
    private readonly ScopedPbrMaterialFixture material = new();
    private readonly ShaderTestFramework drawing = new();
    private readonly WorldPartitionModSystem partitions = new();
    private readonly LumOnDebugShaderProgram? shader;
    private readonly LumOnDebugRenderer? debug;
    private LumonSceneRelightUpdateRenderer relight = null!;
    private readonly DebugViewController? controller;
    private readonly bool productionOwned;
    private readonly bool enclosure, exposedWall;
    private readonly uint[] feedbackPatches;
    private readonly SpatialLightingScene? spatial;
    private readonly int edge;
    public Block SourceBlock => material.Cube;
    private readonly DynamicTexture2D color;
    private readonly DynamicTexture2D depth;
    private readonly GpuFramebuffer output;
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
            mapping.Count < (spatial?.Feedback().Length ?? feedbackPatches.Length)) return false;
        using var ready = snapshot.Readiness.MapRange<uint>(0, checked((int)mapping.Keys.Max()+1), MapBufferAccessMask.MapReadBit);
        if (!ready.IsMapped) return false;
        foreach (uint page in mapping.Keys) if (ready.Span[(int)page] == 0) return false;
        return true;
    }

    /// <summary>Observes completion of all requested terrain captures without accepting allocation as successful capture.</summary>
    public bool AllRequestedCaptured()
    {
        if (!Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out var mirror) ||
            mapping.Count < (spatial?.Feedback().Length ?? feedbackPatches.Length)) return false;
        foreach (ulong key in mapping.Values)
        {
            int index = checked((int)LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key) * LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk
                + (int)LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key));
            var flags = LumonScenePageTableEntryPacking.UnpackFlags(mirror[index]);
            if ((flags & LumonScenePageTableEntryPacking.Flags.Resident) == 0 ||
                (flags & (LumonScenePageTableEntryPacking.Flags.NeedsCapture | LumonScenePageTableEntryPacking.Flags.Capturing)) != 0) return false;
        }
        return true;
    }

    #endregion

    #region Runtime setup
    /// <summary>Mocks camera and terrain raster inputs while retaining renderer registration, cache work and viewer selection.</summary>
    public SurfaceCacheRuntimeFixture(int requestedPages = 1, bool exposedWall = false, bool enclosure = false, SpatialLightingScene? spatial = null, bool productionOwned = false)
    {
        this.spatial=spatial; edge=spatial==null?2:4;
        this.productionOwned=productionOwned; this.enclosure=enclosure; this.exposedWall=exposedWall;
        color=DynamicTexture2D.Create(edge,edge,PixelInternalFormat.Rgba16f);
        depth=DynamicTexture2D.Create(edge,edge,PixelInternalFormat.R32f);
        feedbackPatches = enclosure
            ? Enumerable.Range(0,6).SelectMany(axis => Enumerable.Range(0,4).Select(tile =>
                1u + 6u * (uint)((axis%2==0?0:7)*64 + (tile/2)*8 + tile%2) + (uint)axis)).ToArray()
            : Enumerable.Range(0,requestedPages).Select(index=>1u+6u*(uint)index).ToArray();
        material.SetReadiness(true, true, new System.Numerics.Vector3(spatial?.Reflectance ?? 1), (spatial?.Emission ?? 0)/32f);
        Config.LumOn.Enabled = true;
        var cfg = Config.LumOn.LumonScene;
        cfg.Enabled = true; cfg.NearRadiusChunks = cfg.NearRadiusYChunks = cfg.FarRadiusChunks = cfg.FarRadiusYChunks = 0;
        cfg.MaxAtlasCount = 1; cfg.NearPagesPerChunkBudget = requestedPages; cfg.FarPagesPerChunkBudget = 1;
        cfg.NearTexelsPerVoxelFaceEdge = 1;
        cfg.TraceScene.ClipmapResolution = 32;
        cfg.RelightMaxPagesPerFrame = 1; cfg.RelightTexelsPerPagePerFrame = 64; cfg.RelightRaysPerTexel = 1; cfg.RelightMaxDdaSteps = 256;
        LumOnCameraState? Camera() => spatial?.Camera ?? new LumOnCameraState(0, 32, 0, 0, 32, 0, 0);
        if(spatial!=null) { cfg.NearRadiusChunks=cfg.FarRadiusChunks=2; cfg.NearPagesPerChunkBudget=48; }
        var world = RuntimeRenderEvents.Adapt<IClientWorldAccessor>((method, _) => method.Name switch
        {
            "get_Player" => null, "get_MapSizeY" => 256, "get_Calendar" => null,
            _ => throw new NotSupportedException("World: " + method.Name)
        });
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        depth.UploadDataImmediate(Enumerable.Repeat(.5f,edge*edge).ToArray());
        output = GpuFramebuffer.CreateSingle(color)!;
        var framebuffers = Enumerable.Repeat<FrameBufferRef>(null!, Enum.GetValues<EnumFrameBuffer>().Max(v => (int)v) + 1).ToList();
        framebuffers[(int)EnumFrameBuffer.Primary] = new() { FboId = output.FboId, Width = edge, Height = edge, DepthTextureId = depth.TextureId, ColorTextureIds = [color.TextureId] };
        var render = RuntimeRenderEvents.Adapt<IRenderAPI>((method, args) => method.Name switch
        {
            "get_FrameWidth" or "get_FrameHeight" => edge,
            "get_CameraMatrixOriginf" when spatial!=null => spatial.View(),
            "get_CameraMatrixOriginf" or "get_CurrentProjectionMatrix" => identity,
            "get_FrameBuffers" => framebuffers,
            "get_AmbientColor" => new Vintagestory.API.MathTools.Vec3f(0, 0, 0),
            "get_ShaderUniforms" => new DefaultShaderUniforms { ZNear = .1f, ZFar = 100 },
            "UploadMesh" => new RuntimeMesh(),
            "DeleteMesh" => DeleteMesh((MeshRef)args![0]!),
            "RenderMesh" => Draw(),
            _ => throw new NotSupportedException("Render: " + method.Name)
        });
        LumOnDebugShaderProgram? selected = null;
        var shaders = RuntimeRenderEvents.Adapt<IShaderAPI>((method, _) => method.Name == "GetProgramByName" ? selected : throw new NotSupportedException(method.Name));
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name switch
        {
            "get_Event" => Events.Api, "get_World" => world, "get_Render" => render, "get_Shader" => shaders,
            _ => method.Invoke(assets.Api, args)
        });
        Api = api;
        partitions.StartClientSide(api);
        Buffers = new(api);
        Assert.True(Buffers.EnsureBuffers(edge, edge));
        if (productionOwned) return;
        shader = new() { PassName = "lumon_debug_gbuffer", VertexShader = new Vintagestory.Client.NoObf.Shader(), FragmentShader = new Vintagestory.Client.NoObf.Shader() };
        shader.Initialize(api);
        Assert.True(shader.CompileAndLink(), string.Join('\n', Logs));
        selected = shader;
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
        var source = new RuntimeTraceGeometrySource((x, y, z) => new(
            (spatial?.Solid(x,y,z) ?? (enclosure ? x<=0 || x>=7 || y<=32 || y>=39 || z<=0 || z>=7 : !exposedWall || x<=0)) ? 2u | id << 2 : 1u,
            LumonSceneOccupancyPacking.PackClamped(spatial?.Light(x,y,z) ?? BlockLight, 0, 0, (int)id), 0),
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
            using var binding=GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D,0,Buffers.PatchIdTextureId);
            GL.TexSubImage2D(TextureTarget.Texture2D,0,0,0,edge,edge,PixelFormat.RgbaInteger,PixelType.UnsignedInt,pixels);
        }
        else if (Feedback.TryGetNearChunkSlotAndGeneration(new(0, 1, 0), out uint slot, out ushort generation))
        {
            uint[] pixels = Enumerable.Range(0, 4).SelectMany(index => new uint[] { slot, feedbackPatches[(Frames*4+index)%feedbackPatches.Length], 0, generation }).ToArray();
            using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, Buffers.PatchIdTextureId);
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 2, 2, PixelFormat.RgbaInteger, PixelType.UnsignedInt, pixels);
        }
        Events.Render(EnumRenderStage.Done);
        output.BindWithViewport();
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
        for (int frame = 0; frame < maximumFrames && !condition(); frame++) Frame();
        Feedback.TryGetSelfCheckLine(out string feedback);
        relight.TryGetSelfCheckLine(out string relightState);
        string sources = string.Join(",", Sources.Select(source => $"captures={source.CaptureCount},disposed={source.Disposed}"));
        Assert.True(condition(), $"Runtime condition did not settle within {maximumFrames} frames. Feedback: {feedback}. Relight: {relightState}. Sources: {sources}. Geometry: revision={Geometry.Resources?.Revision}, invalidation={Geometry.Resources?.InvalidationRevision}. Executed: {string.Join(", ", Events.Executed.TakeLast(16))}. Logs: {string.Join(" | ", Logs)}");
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
    public void RequestAtlasRecreation() => Config.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge = 2;

    /// <summary>Changes the source field and versions captured chunks so production invalidates dependent lighting.</summary>
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
        output.BindWithViewport();
        float[] result = new float[edge*edge*4];
        GL.ReadPixels(0, 0, edge, edge, PixelFormat.Rgba, PixelType.Float, result);
        return result;
    }

    /// <summary>Implements the engine mesh draw using a real fullscreen GPU triangle.</summary>
    private object? Draw() { Draws++; drawing.RenderQuad(shader!.ProgramId); return null; }
    /// <summary>Releases the engine mesh token owned by the debug renderer.</summary>
    private static object? DeleteMesh(MeshRef mesh) { mesh.Dispose(); return null; }
    #endregion

    #region Teardown
    /// <summary>Disposes production owners before their mocked engine and material dependencies.</summary>
    public void Dispose()
    {
        controller?.Dispose(); debug?.Dispose();
        if (!productionOwned) { relight.Dispose(); Feedback.Dispose(); Geometry.Dispose(); }
        partitions.Dispose();
        shader?.Dispose(); Buffers.Dispose(); output.Dispose(); color.Dispose(); depth.Dispose(); drawing.Dispose(); assets.Dispose(); material.Dispose();
    }

    /// <summary>Represents the engine-owned mesh handle while draw submission is provided by the GPU fixture.</summary>
    private sealed class RuntimeMesh : MeshRef
    {
        public override bool Initialized => !Disposed;
    }
    #endregion
}
