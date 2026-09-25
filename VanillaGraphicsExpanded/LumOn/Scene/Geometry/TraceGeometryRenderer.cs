using System;
using System.Diagnostics;
using System.Linq;
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
    private readonly System.Func<TraceGeometryMaterials, ITraceGeometrySource> createSource;
    private readonly System.Func<LumOnCameraState?> readCamera;
    private ITraceGeometrySource? source;
    private TraceGeometryPartition? partition;
    private TraceGeometryCoverage? plan;
    private int materialGeneration = -1;
    private int? surfaceResolution;
    private string? failure;
    private int reportedLevels = -1;
    private long nextMetrics, updateCount;
    private double totalUpdate, peakUpdate;
    public TraceGeometryRuntimeMetrics? Metrics { get; private set; }
    public TraceGeometryGpuScene? Resources { get; private set; }
    public double RenderOrder => 9.9;
    public int RenderRange => 1;

    #region Lifetime
    /// <summary>Runs before screen tracing and later surface-cache consumers.</summary>
    public TraceGeometryRenderer(ICoreClientAPI api, VgeConfig config, WorldPartitionModSystem partitions)
        : this(api, config, partitions, materials => new TraceGeometryWorldSource(api, materials), () => LumOnCameraState.Read(api)) { }

    /// <summary>Allows a world source adapter while retaining production planning, publication and event ownership.</summary>
    internal TraceGeometryRenderer(ICoreClientAPI api, VgeConfig config, WorldPartitionModSystem partitions,
        System.Func<TraceGeometryMaterials, ITraceGeometrySource> createSource,
        System.Func<LumOnCameraState?>? readCamera = null)
    {
        this.api = api; this.config = config; this.partitions = partitions;
        this.createSource = createSource;
        this.readCamera = readCamera ?? (() => LumOnCameraState.Read(api));
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
        Resources = null; plan = null; Metrics = null; TraceGeometryRuntimeMetrics.Current = null;
        totalUpdate = peakUpdate = 0; updateCount = 0;
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
        LumOnCameraState? cameraState = readCamera();
        if (!config.LumOn.Enabled || cameraState is not { } camera)
        { Release(); partitions.Pump(); return; }
        var trace = config.LumOn.LumonScene.TraceScene;
        int? surface = config.LumOn.LumonScene.Enabled ? trace.ClipmapResolution : null;
        if (reportedLevels != trace.ClipmapLevels)
        {
            reportedLevels = trace.ClipmapLevels;
            api.Logger.Notification("[VGE] Shared TraceScene uses L0 only; TraceScene ClipmapLevels={0} is retired.", reportedLevels);
        }
        long started = Stopwatch.GetTimestamp();
        bool pumped = false;
        try
        {
            if (camera.Dimension != 0) throw new InvalidOperationException("Shared geometry supports the primary world only.");
            var next = TraceGeometryCoverage.Plan(new(camera.CameraX, camera.CameraY, camera.CameraZ), true, surface, api.World.MapSizeY);
            int generation = PbrMaterialRegistry.Instance.GeometryGeneration;
            if (Resources == null || surfaceResolution != surface || materialGeneration != generation)
            {
                Release();
                var materials = new TraceGeometryMaterials();
                source = createSource(materials);
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
        SampleMetrics(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
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

    /// <summary>Reports actual shared residency instead of the retired region scheduler.</summary>
    public string DumpTraceSceneSchedulerState(int topN) => JsonSerializer.Serialize(new
    { SharedGeometry = Metrics, Failure = failure, Partitions = partitions.GetCoordinator().Diagnostics() });

    /// <summary>Samples demand separately from residency and keeps diagnostic serialization outside update timing.</summary>
    private void SampleMetrics(double milliseconds)
    {
        if (partition == null || source == null || plan == null || Resources == null) return;
        totalUpdate += milliseconds; peakUpdate = Math.Max(peakUpdate, milliseconds); updateCount++;
        if (Environment.TickCount64 < nextMetrics) return;
        nextMetrics = Environment.TickCount64 + 1000;
        var layout = new PartitionLayout(new(16, 16, 16));
        var near = plan.NearField is { } n ? layout.Intersecting(plan.Clip(n)).ToHashSet() : new System.Collections.Generic.HashSet<PartitionCoordinate>();
        var surface = plan.Surface is { } s ? layout.Intersecting(plan.Clip(s)).ToHashSet() : new System.Collections.Generic.HashSet<PartitionCoordinate>();
        Metrics = new(partition.Instance, Resources.Resolution, source.Cache.SourceReads, source.Cache.InFlight,
            source.Cache.SnapshotBytes, partition.StagedPayloadBytes, Resources.TextureBytes, Resources.UploadedBytes,
            partition.PublishedCells, milliseconds, totalUpdate / updateCount, peakUpdate,
            near.Count, surface.Count, near.Count(surface.Contains), partitions.GetCoordinator().Statistics(partition.Instance), Resources.CaptureIdentityBytes);
        TraceGeometryRuntimeMetrics.Current = Metrics;
        if (partitions.RecordDiagnostics) api.Logger.Notification("[VGE PartitionMetrics] {0}", DumpTraceSceneSchedulerState(0));
    }
    #endregion
}
