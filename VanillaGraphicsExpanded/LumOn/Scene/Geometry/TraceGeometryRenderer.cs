using System;
using System.Text.Json;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Owns the shared geometry generation, game notifications and single coordinator pump.</summary>
internal sealed class TraceGeometryRenderer : IRenderer, ITraceGeometrySceneProvider
{
    private readonly ICoreClientAPI api;
    private readonly VgeConfig config;
    private readonly WorldPartitionModSystem partitions;
    private readonly IEventAPI events;
    private TraceGeometryWorldSource? source;
    private TraceGeometryPartition? partition;
    private TraceGeometryCoverage? plan;
    private int materialGeneration = -1;
    private int? surfaceResolution;
    private string? failure;
    private int reportedLevels = -1;
    public TraceGeometryGpuScene? Resources { get; private set; }
    public double RenderOrder => 9.9;
    public int RenderRange => 1;

    #region Lifetime
    /// <summary>Runs before screen tracing and later surface-cache consumers.</summary>
    public TraceGeometryRenderer(ICoreClientAPI api, VgeConfig config, WorldPartitionModSystem partitions)
    {
        this.api = api; this.config = config; this.partitions = partitions;
        events = ((ICoreAPI)api).Event;
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_shared_trace_geometry");
        api.Event.LeaveWorld += Release;
        events.ChunkDirty += OnChunkDirty;
    }

    /// <summary>Retires coordinator leases before releasing workers and textures.</summary>
    private void Release()
    {
        if (partition == null) Resources?.Dispose();
        partition?.Dispose(); partition = null;
        source?.Dispose(); source = null;
        Resources = null; plan = null;
    }

    /// <summary>Unsubscribes game callbacks and ends the current generation.</summary>
    public void Dispose()
    {
        api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        api.Event.LeaveWorld -= Release; events.ChunkDirty -= OnChunkDirty;
        Release();
    }
    #endregion

    #region Frame and dependency updates
    /// <summary>Plans both domains, recreates changed generations and services only coordinator-authorized work.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!config.LumOn.Enabled || api.World?.Player?.Entity is not { } entity)
        { Release(); partitions.Pump(); return; }
        var trace = config.LumOn.LumonScene.TraceScene;
        int? surface = config.LumOn.LumonScene.Enabled ? trace.ClipmapResolution : null;
        if (reportedLevels != trace.ClipmapLevels)
        {
            reportedLevels = trace.ClipmapLevels;
            api.Logger.Notification("[VGE] Shared TraceScene uses L0 only; TraceScene ClipmapLevels={0} is retired.", reportedLevels);
        }
        bool pumped = false;
        try
        {
            if (entity.Pos.Dimension != 0) throw new InvalidOperationException("Shared geometry supports the primary world only.");
            var camera = entity.CameraPos;
            var next = TraceGeometryCoverage.Plan(new(camera.X, camera.Y, camera.Z), true, surface, api.World.MapSizeY);
            int generation = PbrMaterialRegistry.Instance.GeometryGeneration;
            if (Resources == null || surfaceResolution != surface || materialGeneration != generation)
            {
                Release();
                var materials = new TraceGeometryMaterials();
                source = new(api, materials);
                Resources = new(next.Resolution);
                partition = new(partitions.GetCoordinator(), source.Cache, materials, Resources);
                surfaceResolution = surface; materialGeneration = generation;
            }
            plan = next;
            source!.Prepare(plan); partition!.Prepare(plan);
            pumped = true; partitions.Pump(); partition.Service(plan);
            failure = null;
        }
        catch (Exception ex)
        {
            Release();
            if (failure != ex.Message) api.Logger.Warning("[VGE] Shared trace geometry unavailable: {0}", ex.Message);
            failure = ex.Message;
        }
        finally { if (!pumped) partitions.Pump(); }
    }

    /// <summary>Rechecks edits and loaded identities immediately before a consumer binds the scene.</summary>
    public TraceGeometryGpuScene? PrepareScene()
    {
        if (!config.LumOn.Enabled || materialGeneration != PbrMaterialRegistry.Instance.GeometryGeneration) return null;
        if (plan != null) { source!.Prepare(plan); partition!.Prepare(plan); }
        return Resources;
    }

    /// <summary>Versions one shared source for all dependent consumers.</summary>
    private void OnChunkDirty(Vec3i c, IWorldChunk chunk, EnumChunkDirtyReason reason) =>
        source?.MarkDirty(ChunkKey.FromChunkCoords(c.X, c.Y, c.Z));

    /// <summary>Reports the surface logical box for temporary existing diagnostic bindings.</summary>
    public bool TryGetLevel0RuntimeParams(out VectorInt3 origin, out VectorInt3 ring, out int resolution)
    {
        origin = ring = default; resolution = 0;
        if (plan?.Surface is not { } surface || Resources == null) return false;
        origin = new((int)surface.Min.X, (int)surface.Min.Y, (int)surface.Min.Z);
        resolution = Resources.Resolution;
        ring = new((origin.X % resolution + resolution) % resolution, (origin.Y % resolution + resolution) % resolution, (origin.Z % resolution + resolution) % resolution);
        return true;
    }

    /// <summary>Reports actual shared residency instead of the retired region scheduler.</summary>
    public string DumpTraceSceneSchedulerState(int topN) => JsonSerializer.Serialize(new
    { SharedGeometry = partition?.Instance, Failure = failure, SourceReads = source?.Cache.SourceReads, Partitions = partitions.GetCoordinator().Diagnostics() });

    /// <summary>Provides a compact shared-source status for the existing diagnostics command.</summary>
    public bool TryGetTraceSceneSchedulerTopKLine(int k, out string line)
    { line = $"Shared geometry: sources={source?.Cache.SourceReads ?? 0}, inFlight={source?.Cache.InFlight ?? 0}"; return Resources != null; }
    #endregion
}
