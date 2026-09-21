using System;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;

/// <summary>Composition and render-thread lifetime of the independent local geometry partition.</summary>
internal sealed class LocalGeometryPartitionRenderer : IRenderer, ILocalTraceSceneProvider
{
    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly WorldPartitionModSystem partitions;
    private readonly IEventAPI events;
    private readonly LocalTraceCoveragePolicy coverage = new();
    private LocalTraceWorldSource? source;
    private LocalTraceGpuScene? scene;
    private LocalGeometryPartition? provider;
    private long instance;
    private double lastReach;
    private string? lastReportedFailure;
    public string? CoverageFailure { get; private set; }
    public double RenderOrder => 9.9;
    public int RenderRange => 1;

    #region Lifetime
    /// <summary>Registers before screen-probe tracing without depending on the occupancy renderer.</summary>
    public LocalGeometryPartitionRenderer(ICoreClientAPI capi, VgeConfig config, WorldPartitionModSystem partitions)
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
    }

    /// <summary>Creates a fresh immutable material/source generation for this world lifetime.</summary>
    private void Create(in LocalTraceCoveragePlan plan)
    {
        Release();
        var materials = new LocalTraceMaterialRegistry();
        source = new LocalTraceWorldSource(capi, materials);
        scene = new LocalTraceGpuScene(plan.Resolution, LocalTraceCoveragePolicy.CellSize);
        provider = new LocalGeometryPartition(source, scene, materials);
        int cells = scene.RegionResolution * scene.RegionResolution * scene.RegionResolution;
        instance = partitions.GetCoordinator().Register("Local geometry", "primary", new(new(16, 16, 16)),
            new(coverage.PrefetchMargin, coverage.PrefetchMargin, 2), new(cells, 64, 64, 32, 8L * 1024 * 1024), provider);
        lastReach = plan.TraceReach;
    }
    #endregion

    #region Render and source updates
    /// <summary>Updates required coverage from supported origins and trace reach, then publishes budgeted cells.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!config.LumOn.Enabled || capi.World?.Player?.Entity is not { } entity)
        {
            Release();
            return;
        }
        var camera = entity.CameraPos;
        double reach = LocalTraceCoveragePolicy.MaximumTraceReach(config.LumOn.RayMaxDistance,
            config.WorldProbeClipmap.ClipmapBaseSpacing, config.WorldProbeClipmap.LevelsClamped);
        var position = new PartitionPoint(camera.X, camera.Y, camera.Z);
        // This source adapter uses the primary world; never alias another dimension into the same GPU identity.
        if (entity.Pos.Dimension != 0 || !coverage.TryPlan(position, reach, out LocalTraceCoveragePlan plan))
        {
            CoverageFailure = "Local geometry source domain exceeds the supported dimension or bounded GPU envelope.";
            if (CoverageFailure != lastReportedFailure) capi.Logger.Warning("[VGE] {0}", CoverageFailure);
            lastReportedFailure = CoverageFailure;
            Release();
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
        if (provider.UnsupportedCellCount > 0) CoverageFailure = "Local geometry requested cells outside its contiguous GPU source envelope.";
        if (CoverageFailure != lastReportedFailure)
        {
            if (CoverageFailure != null) capi.Logger.Warning("[VGE] {0}", CoverageFailure);
            lastReportedFailure = CoverageFailure;
        }
    }

    /// <summary>Rejects newly dirty content before any consumer samples the previously published generation.</summary>
    public LocalTraceGpuScene? PrepareLocalTraceScene()
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
