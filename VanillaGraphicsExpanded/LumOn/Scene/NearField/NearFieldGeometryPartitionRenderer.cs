using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Composition and render-thread lifetime of the independent near-field geometry partition.</summary>
internal sealed class NearFieldGeometryPartitionRenderer : IRenderer, INearFieldSceneProvider
{
    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly WorldPartitionModSystem partitions;
    private readonly IEventAPI events;
    private readonly NearFieldCoveragePolicy coverage = new();
    private NearFieldWorldSource? source;
    private NearFieldGpuScene? scene;
    private NearFieldGeometryPartition? provider;
    private long instance;
    private double lastReach;
    private string? lastReportedFailure;
    private long nextMetricsSample;
    private long updateCount;
    private double peakUpdateMilliseconds;
    private double totalUpdateMilliseconds;
    public NearFieldRuntimeMetrics? Metrics { get; private set; }
    public string? CoverageFailure { get; private set; }
    public double RenderOrder => 9.9;
    public int RenderRange => 1;

    #region Lifetime
    /// <summary>Registers before screen-probe tracing without depending on the occupancy renderer.</summary>
    public NearFieldGeometryPartitionRenderer(ICoreClientAPI capi, VgeConfig config, WorldPartitionModSystem partitions)
    {
        this.capi = capi; this.config = config; this.partitions = partitions;
        events = ((ICoreAPI)capi).Event;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_local_geometry_partition");
        capi.Event.LeaveWorld += Release;
        events.ChunkDirty += OnChunkDirty;
    }

    /// <summary>Unsubscribes callbacks and retires the registration before disposing GPU storage.</summary>
    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        capi.Event.LeaveWorld -= Release;
        events.ChunkDirty -= OnChunkDirty;
        Release();
    }

    /// <summary>Ends all cell lifetimes before their backing sources and textures disappear.</summary>
    private void Release()
    {
        if (instance != 0) partitions.GetCoordinator().Unregister(instance);
        instance = 0;
        provider = null;
        source?.Dispose(); source = null;
        scene?.Dispose(); scene = null;
        Metrics = null;
        updateCount = 0;
        peakUpdateMilliseconds = 0;
        totalUpdateMilliseconds = 0;
    }

    /// <summary>Creates a fresh immutable material/source generation for this world lifetime.</summary>
    private void Create(in NearFieldCoveragePlan plan)
    {
        Release();
        var materials = new NearFieldMaterialRegistry();
        source = new NearFieldWorldSource(capi, materials);
        scene = new NearFieldGpuScene(plan.Resolution, NearFieldCoveragePolicy.CellSize);
        provider = new NearFieldGeometryPartition(source, scene, materials);
        int cells = scene.RegionResolution * scene.RegionResolution * scene.RegionResolution;
        instance = partitions.GetCoordinator().Register("Near-field geometry", "primary", new(new(16, 16, 16)),
            new(0, 0, 0), new(cells, 64, 64, 32, 8L * 1024 * 1024), provider);
        lastReach = plan.TraceReach;
    }
    #endregion

    #region Render and source updates
    /// <summary>Updates required coverage from supported origins and trace reach, then publishes budgeted cells.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        long started = Stopwatch.GetTimestamp();
        if (!config.LumOn.Enabled || capi.World?.Player?.Entity is not { } entity)
        {
            Release();
            partitions.Pump();
            return;
        }
        var camera = entity.CameraPos;
        double reach = NearFieldCoveragePolicy.MaximumTraceReach(config.LumOn.RayMaxDistance,
            config.WorldProbeClipmap.ClipmapBaseSpacing, config.WorldProbeClipmap.LevelsClamped);
        var position = new PartitionPoint(camera.X, camera.Y, camera.Z);
        // This source adapter uses the primary world; never alias another dimension into the same GPU identity.
        if (entity.Pos.Dimension != 0 || !coverage.TryPlan(position, reach, out NearFieldCoveragePlan plan, capi.World.MapSizeY))
        {
            CoverageFailure = "Near-field geometry source domain exceeds the supported dimension or bounded GPU envelope.";
            if (CoverageFailure != lastReportedFailure) capi.Logger.Warning("[VGE] {0}", CoverageFailure);
            lastReportedFailure = CoverageFailure;
            Release();
            partitions.Pump();
            return;
        }
        CoverageFailure = null;
        if (scene == null || scene.Resolution != plan.Resolution || lastReach != reach) Create(plan);
        scene!.SetWindow(plan.WindowOrigin);
        scene.SupportedOrigins = plan.Origins;
        // Round outward so shader float conversion cannot reject a mathematically covered handoff distance.
        scene.MaximumTraceReach = (float)Math.Ceiling(plan.TraceReach);
        var min = new PartitionCoordinate(plan.WindowOrigin.X / 16, plan.WindowOrigin.Y / 16, plan.WindowOrigin.Z / 16);
        source!.BeginFrame(new(min, new(min.X + scene.RegionResolution, min.Y + scene.RegionResolution, min.Z + scene.RegionResolution)));
        provider!.RefreshDependencies(partitions.GetCoordinator());
        partitions.GetCoordinator().SetSource(new(1, instance, "primary", position, plan.Required));
        partitions.Pump();
        if (provider.UnsupportedCellCount > 0) CoverageFailure = "Near-field geometry requested cells outside its contiguous GPU source envelope.";
        if (CoverageFailure != lastReportedFailure)
        {
            if (CoverageFailure != null) capi.Logger.Warning("[VGE] {0}", CoverageFailure);
            lastReportedFailure = CoverageFailure;
        }
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        peakUpdateMilliseconds = Math.Max(peakUpdateMilliseconds, elapsed);
        updateCount++;
        totalUpdateMilliseconds += elapsed;
        // Sampling and log serialization are excluded from update timing and occur at most once per second.
        if (Environment.TickCount64 >= nextMetricsSample)
        {
            nextMetricsSample = Environment.TickCount64 + 1000;
            Metrics = new(instance, position, scene.Resolution, coverage.OriginRadius, coverage.PrefetchMargin,
                source.SourceReads, source.CacheHits, source.InFlight, source.ResidentSnapshotBytes,
                scene.TextureStorageBytes, scene.UploadedBytes, scene.PublishedCells, elapsed,
                totalUpdateMilliseconds / updateCount, peakUpdateMilliseconds, updateCount,
                source.CaptureCount, source.CaptureMilliseconds, source.PeakCaptureMilliseconds);
            if (partitions.RecordDiagnostics)
                capi.Logger.Notification("[VGE PartitionMetrics] {0}", JsonSerializer.Serialize(new
                {
                    NearField = Metrics,
                    Partitions = partitions.GetCoordinator().Diagnostics()
                        .Select(p => new { p.Instance, p.Name, p.Statistics, p.RequiredReady, p.OldestRequiredWaitTicks,
                            p.PublicationCount, p.MeanPublicationWaitTicks, p.MaximumPublicationWaitTicks })
                }));
        }
    }

    /// <summary>Rejects newly dirty content before any consumer samples the previously published generation.</summary>
    public NearFieldGpuScene? PrepareNearFieldScene()
    {
        if (!config.LumOn.Enabled) return null;
        provider?.RefreshDependencies(partitions.GetCoordinator());
        return scene;
    }

    /// <summary>Records dirty source chunks; all eight dependent cells observe the changed version.</summary>
    private void OnChunkDirty(Vec3i coordinate, IWorldChunk chunk, EnumChunkDirtyReason reason) =>
        source?.MarkDirty(ChunkKey.FromChunkCoords(coordinate.X, coordinate.Y, coordinate.Z));

    #endregion
}
