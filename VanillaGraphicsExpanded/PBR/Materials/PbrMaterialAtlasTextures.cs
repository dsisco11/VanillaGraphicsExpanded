using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR.Materials.Async;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Builds per-block-atlas GPU data textures that store base PBR material parameters per texel.
/// Sampled by patched vanilla chunk shaders using the same atlas UVs as <c>terrainTex</c>.
/// </summary>
internal sealed class PbrMaterialAtlasTextures : IDisposable
{
    public static PbrMaterialAtlasTextures Instance { get; } = new();

    private readonly MaterialAtlasTextureStore textureStore = new();
    private bool isDisposed;

    private short lastBlockAtlasReloadIteration = short.MinValue;
    private int lastBlockAtlasNonNullPositions = -1;
    private short lastScheduledAtlasReloadIteration = short.MinValue;
    private int lastScheduledAtlasNonNullPositions = -1;
    private readonly object schedulerLock = new();
    private ICoreClientAPI? capi;
    private PbrMaterialAtlasBuildScheduler? scheduler;
    private PbrMaterialAtlasBuildSchedulerRenderer? schedulerRenderer;
    private bool schedulerRegistered;
    private int sessionGeneration;
    private bool buildRequested;

    private bool texturesCreated;

    private PbrMaterialAtlasTextures() { }

    public bool IsInitialized { get; private set; }
    public bool AreTexturesCreated => texturesCreated;
    public bool IsBuildComplete { get; private set; }

    public void RebakeNormalDepthAtlas(ICoreClientAPI capi)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));
        if (!ConfigModSystem.Config.EnableNormalDepthAtlas)
        {
            return;
        }

        CreateTextureObjects(capi);
        if (!texturesCreated)
        {
            return;
        }

        IBlockTextureAtlasAPI atlas = capi.BlockTextureAtlas;

        AtlasSnapshot snapshot = AtlasSnapshot.Capture(atlas);
        if (snapshot.Pages.Count == 0)
        {
            return;
        }

        TextureAtlasPosition? TryGetAtlasPosition(AssetLocation loc)
        {
            try
            {
                return atlas[loc];
            }
            catch
            {
                return null;
            }
        }

        var planner = new MaterialAtlasNormalDepthBuildPlanner();
        var plan = planner.CreatePlan(
            snapshot,
            TryGetAtlasPosition,
            PbrMaterialRegistry.Instance,
            capi.Assets.GetLocations("textures/block/", domain: null));

        var runner = new MaterialAtlasNormalDepthBuildRunner(textureStore, new MaterialOverrideTextureLoader());
        _ = runner.ExecutePlan(capi, plan);

        if (ConfigModSystem.Config.DebugLogNormalDepthAtlas)
        {
            capi.Logger.Debug("[VGE] Normal+depth atlas rebake requested (forced)");
        }
    }

    /// <summary>
    /// Compatibility wrapper: creates/updates textures and populates their contents.
    /// Prefer <see cref="CreateTextureObjects"/> + <see cref="PopulateAtlasContents"/>.
    /// </summary>
    public void Initialize(ICoreClientAPI capi)
        => PopulateAtlasContents(capi);

    /// <summary>
    /// Phase 1: allocate the GPU textures for the current block atlas pages.
    /// Does not compute/upload per-tile material params and does not run the normal+depth bake.
    /// </summary>
    public void CreateTextureObjects(ICoreClientAPI capi)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));

        this.capi = capi;
        EnsureSchedulerRegistered(capi);

        IBlockTextureAtlasAPI atlas = capi.BlockTextureAtlas;

        var atlasPages = new List<(int atlasTextureId, int width, int height)>(capacity: atlas.AtlasTextures.Count);
        foreach (LoadedTexture atlasPage in atlas.AtlasTextures)
        {
            if (atlasPage.TextureId == 0 || atlasPage.Width <= 0 || atlasPage.Height <= 0)
            {
                continue;
            }

            atlasPages.Add((atlasPage.TextureId, atlasPage.Width, atlasPage.Height));
        }

        if (atlasPages.Count == 0)
        {
            // Block atlas not ready yet.
            texturesCreated = false;
            IsInitialized = false;
            IsBuildComplete = false;
            return;
        }

        if (textureStore.NeedsResync(atlasPages, ConfigModSystem.Config.EnableNormalDepthAtlas))
        {
            CancelActiveBuildSession();
        }

        textureStore.SyncToAtlasPages(atlasPages, ConfigModSystem.Config.EnableNormalDepthAtlas);

        texturesCreated = textureStore.HasAnyTextures;
        IsInitialized = texturesCreated;
        IsBuildComplete = false;
    }

    /// <summary>
    /// Phase 2: compute and upload material params, then (optionally) bake normal+depth.
    /// Intended to run after the block atlas is finalized (e.g. on BlockTexturesLoaded).
    /// </summary>
    public void PopulateAtlasContents(ICoreClientAPI capi)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));

        this.capi = capi;
        EnsureSchedulerRegistered(capi);

        // Guard: avoid repopulating when nothing changed.
        // Note: some mods may insert additional textures into the atlas after BlockTexturesLoaded.
        // In those cases reloadIteration may remain unchanged, so we also track the non-null position count.
        IBlockTextureAtlasAPI preAtlas = capi.BlockTextureAtlas;
        (short currentReload, int nonNullCount) = GetAtlasStats(preAtlas);

        if (!buildRequested && currentReload >= 0)
        {
            // Don't restart while the current async build is still targeting the same atlas stats.
            if (scheduler?.ActiveSession is not null &&
                currentReload == lastScheduledAtlasReloadIteration &&
                nonNullCount == lastScheduledAtlasNonNullPositions)
            {
                IsBuildComplete = scheduler.ActiveSession.IsComplete;
                return;
            }

            // Don't redo completed work.
            if (IsBuildComplete &&
                currentReload == lastBlockAtlasReloadIteration &&
                nonNullCount == lastBlockAtlasNonNullPositions)
            {
                return;
            }
        }

        buildRequested = false;

        CreateTextureObjects(capi);

        if (!texturesCreated)
        {
            return;
        }

        if (!PbrMaterialRegistry.Instance.IsInitialized)
        {
            capi.Logger.Warning("[VGE] Material atlas textures: registry not initialized; skipping.");
            IsInitialized = texturesCreated;
            IsBuildComplete = false;
            return;
        }

        IBlockTextureAtlasAPI atlas = capi.BlockTextureAtlas;

        var atlasPages = new List<(int atlasTextureId, int width, int height)>(capacity: atlas.AtlasTextures.Count);
        foreach (LoadedTexture atlasPage in atlas.AtlasTextures)
        {
            if (atlasPage.TextureId == 0 || atlasPage.Width <= 0 || atlasPage.Height <= 0)
            {
                continue;
            }

            atlasPages.Add((atlasPage.TextureId, atlasPage.Width, atlasPage.Height));
        }

        TextureAtlasPosition? TryGetAtlasPosition(AssetLocation loc)
        {
            try
            {
                return atlas[loc];
            }
            catch
            {
                return null;
            }
        }

        AtlasSnapshot snapshot = AtlasSnapshot.Capture(atlas);
        var plan = new MaterialAtlasBuildPlanner().CreatePlan(
            snapshot,
            TryGetAtlasPosition,
            PbrMaterialRegistry.Instance,
            blockTextureAssets: Array.Empty<AssetLocation>(),
            enableNormalDepth: false);

        int filledRects;
        int overriddenRects;
        if (ConfigModSystem.Config.EnableMaterialAtlasAsyncBuild)
        {
            filledRects = StartMaterialParamsAsyncBuild(
                atlasPages,
                plan,
                currentReload,
                nonNullCount);
            overriddenRects = 0; // overrides are applied asynchronously on the render thread.
        }
        else
        {
            (filledRects, overriddenRects) = PopulateMaterialParamsSync(
                capi,
                plan);
        }

        if (ConfigModSystem.Config.EnableNormalDepthAtlas)
        {
            snapshot = AtlasSnapshot.Capture(atlas);
            var planner = new MaterialAtlasNormalDepthBuildPlanner();
            var normalDepthPlan = planner.CreatePlan(
                snapshot,
                TryGetAtlasPosition,
                PbrMaterialRegistry.Instance,
                capi.Assets.GetLocations("textures/block/", domain: null));

            var runner = new MaterialAtlasNormalDepthBuildRunner(textureStore, new MaterialOverrideTextureLoader());
            (int bakedRects, int appliedOverrides) = runner.ExecutePlan(capi, normalDepthPlan);

            if (appliedOverrides > 0)
            {
                capi.Logger.Notification(
                    "[VGE] Normal+height overrides applied: {0}",
                    appliedOverrides);
            }

            if (ConfigModSystem.Config.DebugLogNormalDepthAtlas)
            {
                capi.Logger.Debug(
                    "[VGE] Normal+depth atlas plan: pages={0} bakeJobs={1} overrideJobs={2} skippedByOverrides={3}",
                    normalDepthPlan.Pages.Count,
                    normalDepthPlan.PlanStats.BakeJobs,
                    normalDepthPlan.PlanStats.OverrideJobs,
                    normalDepthPlan.PlanStats.SkippedByOverrides);

                capi.Logger.Debug(
                    "[VGE] Normal+depth atlas execution: bakedRects={0} appliedOverrides={1}",
                    bakedRects,
                    appliedOverrides);
            }
        }

        capi.Logger.Notification(
            ConfigModSystem.Config.EnableMaterialAtlasAsyncBuild
                ? "[VGE] Material params async build scheduled: {0} atlas page(s), {1} tile(s) queued ({2} overrides deferred)"
                : "[VGE] Built material param atlas textures: {0} atlas page(s), {1} texture rect(s) filled ({2} overridden)",
            textureStore.PageCount,
            filledRects,
            overriddenRects);

        if (ConfigModSystem.Config.DebugLogNormalDepthAtlas)
        {
            var sizeCounts = new Dictionary<(int Width, int Height), int>();
            foreach ((int _, int width, int height) in atlasPages)
            {
                var key = (width, height);
                if (sizeCounts.TryGetValue(key, out int count))
                {
                    sizeCounts[key] = count + 1;
                }
                else
                {
                    sizeCounts[key] = 1;
                }
            }

            capi.Logger.Debug(
                "[VGE] Normal+depth atlas: enabled={0}, pages={1}, pageSizes=[{2}]",
                ConfigModSystem.Config.EnableNormalDepthAtlas,
                textureStore.PageCount,
                string.Join(", ", sizeCounts.Select(kvp => $"{kvp.Key.Width}x{kvp.Key.Height}*{kvp.Value}")));
        }

        IsInitialized = textureStore.PageCount > 0;
        IsBuildComplete = !ConfigModSystem.Config.EnableMaterialAtlasAsyncBuild || (scheduler?.ActiveSession?.IsComplete ?? false);
        if (currentReload >= 0)
        {
            lastBlockAtlasReloadIteration = currentReload;
            lastBlockAtlasNonNullPositions = nonNullCount;
        }
    }

    private static (short reloadIteration, int nonNullCount) GetAtlasStats(IBlockTextureAtlasAPI atlas)
    {
        if (atlas?.Positions is null || atlas.Positions.Length == 0)
        {
            return (-1, 0);
        }

        short reloadIteration = -1;
        int nonNullCount = 0;

        foreach (TextureAtlasPosition? pos in atlas.Positions)
        {
            if (pos is null) continue;
            nonNullCount++;

            if (reloadIteration < 0)
            {
                reloadIteration = pos.reloadIteration;
            }
        }

        return (reloadIteration, nonNullCount);
    }

    public void RequestRebuild(ICoreClientAPI capi)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));

        this.capi = capi;
        buildRequested = true;
        CancelActiveBuildSession();
    }

    public void CancelActiveBuildSession()
    {
        lock (schedulerLock)
        {
            scheduler?.CancelActiveSession();
            lastScheduledAtlasReloadIteration = short.MinValue;
            lastScheduledAtlasNonNullPositions = -1;
            IsBuildComplete = false;
        }
    }

    private void EnsureSchedulerRegistered(ICoreClientAPI capi)
    {
        lock (schedulerLock)
        {
            scheduler ??= new PbrMaterialAtlasBuildScheduler();
            scheduler.Initialize(capi, TryGetPageTexturesByAtlasTexId);

            if (schedulerRegistered)
            {
                return;
            }

            schedulerRenderer ??= new PbrMaterialAtlasBuildSchedulerRenderer(scheduler);
            capi.Event.RegisterRenderer(schedulerRenderer, EnumRenderStage.Before, "vge_pbr_material_atlas_async_build");
            schedulerRegistered = true;
        }
    }

    private PbrMaterialAtlasPageTextures? TryGetPageTexturesByAtlasTexId(int atlasTextureId)
        => textureStore.TryGetPageTextures(atlasTextureId, out PbrMaterialAtlasPageTextures pageTextures)
            ? pageTextures
            : null;

    private int StartMaterialParamsAsyncBuild(
        IReadOnlyList<(int atlasTextureId, int width, int height)> atlasPages,
        AtlasBuildPlan plan,
        short currentReload,
        int nonNullCount)
    {
        if (scheduler is null)
        {
            return 0;
        }

        int generationId = Interlocked.Increment(ref sessionGeneration);
        if (generationId <= 0)
        {
            sessionGeneration = 1;
            generationId = 1;
        }

        var cpuJobs = new List<MaterialAtlasParamsCpuTileJob>(capacity: plan.MaterialParamsTiles.Count);
        foreach (AtlasBuildPlan.MaterialParamsTileJob tile in plan.MaterialParamsTiles)
        {
            cpuJobs.Add(new MaterialAtlasParamsCpuTileJob(
                GenerationId: generationId,
                AtlasTextureId: tile.AtlasTextureId,
                Rect: tile.Rect,
                Texture: tile.Texture,
                Definition: tile.Definition,
                Priority: tile.Priority));
        }

        var overrideJobs = new List<MaterialAtlasParamsGpuOverrideUpload>(capacity: plan.MaterialParamsOverrides.Count);
        foreach (AtlasBuildPlan.MaterialParamsOverrideJob ov in plan.MaterialParamsOverrides)
        {
            overrideJobs.Add(new MaterialAtlasParamsGpuOverrideUpload(
                GenerationId: generationId,
                AtlasTextureId: ov.AtlasTextureId,
                Rect: ov.Rect,
                TargetTexture: ov.TargetTexture,
                OverrideAsset: ov.OverrideTexture,
                RuleId: ov.RuleId,
                RuleSource: ov.RuleSource,
                Scale: ov.Scale,
                Priority: ov.Priority));
        }

        var session = new PbrMaterialAtlasBuildSession(
            generationId,
            atlasPages,
            cpuJobs,
            overrideJobs,
            new MaterialOverrideTextureLoader());
        scheduler.StartSession(session);

        lastScheduledAtlasReloadIteration = currentReload;
        lastScheduledAtlasNonNullPositions = nonNullCount;
        IsBuildComplete = cpuJobs.Count == 0 && overrideJobs.Count == 0;

        return cpuJobs.Count;
    }

    private (int filledRects, int overriddenRects) PopulateMaterialParamsSync(
        ICoreClientAPI capi,
        AtlasBuildPlan plan)
    {
        var uploader = new MaterialAtlasParamsUploader(textureStore);
        var overrideLoader = new MaterialOverrideTextureLoader();

        // Upload procedural tiles.
        int filledRects = 0;
        foreach (AtlasBuildPlan.MaterialParamsTileJob tile in plan.MaterialParamsTiles)
        {
            float[] rgb = MaterialAtlasParamsBuilder.BuildRgb16fTile(
                tile.Texture,
                tile.Definition,
                rectWidth: tile.Rect.Width,
                rectHeight: tile.Rect.Height,
                CancellationToken.None);

            if (uploader.TryUploadTile(tile.AtlasTextureId, tile.Rect, rgb))
            {
                filledRects++;
            }
        }

        // Upload explicit material params overrides (if any) after base tiles.
        // Policy: if an override fails to load/validate, keep the procedural output.
        int overriddenRects = 0;
        foreach (AtlasBuildPlan.MaterialParamsOverrideJob ov in plan.MaterialParamsOverrides)
        {
            if (!overrideLoader.TryLoadRgbaFloats01(
                    capi,
                    ov.OverrideTexture,
                    out int _,
                    out int _,
                    out float[] floatRgba01,
                    out string? reason,
                    expectedWidth: ov.Rect.Width,
                    expectedHeight: ov.Rect.Height))
            {
                capi.Logger.Warning(
                    "[VGE] PBR override ignored: rule='{0}' target='{1}' override='{2}' reason='{3}'. Falling back to generated maps.",
                    ov.RuleId ?? "(no id)",
                    ov.TargetTexture,
                    ov.OverrideTexture,
                    reason ?? "unknown error");
                continue;
            }

            float[] rgb = new float[checked(ov.Rect.Width * ov.Rect.Height * 3)];
            MaterialAtlasParamsBuilder.ApplyOverrideToTileRgb16f(
                tileRgbTriplets: rgb,
                rectWidth: ov.Rect.Width,
                rectHeight: ov.Rect.Height,
                overrideRgba01: floatRgba01,
                scale: ov.Scale);

            if (uploader.TryUploadTile(ov.AtlasTextureId, ov.Rect, rgb))
            {
                overriddenRects++;
            }
        }

        if (filledRects == 0 && PbrMaterialRegistry.Instance.MaterialIdByTexture.Count > 0)
        {
            capi.Logger.Warning(
                "[VGE] Built material param atlas textures but filled 0 rects. This usually means atlas key mismatch (e.g. textures/...png vs block/...) or textures not in block atlas.");
        }

        return (filledRects, overriddenRects);
    }

    public bool TryGetMaterialParamsTextureId(int atlasTextureId, out int materialParamsTextureId)
        => textureStore.TryGetMaterialParamsTextureId(atlasTextureId, out materialParamsTextureId);

    public bool TryGetNormalDepthTextureId(int atlasTextureId, out int normalDepthTextureId)
        => textureStore.TryGetNormalDepthTextureId(atlasTextureId, out normalDepthTextureId);

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        CancelActiveBuildSession();

        if (schedulerRegistered && capi is not null && schedulerRenderer is not null)
        {
            try
            {
                capi.Event.UnregisterRenderer(schedulerRenderer, EnumRenderStage.Before);
            }
            catch
            {
                // Best-effort.
            }
        }

        schedulerRenderer?.Dispose();
        schedulerRenderer = null;

        scheduler?.Dispose();
        scheduler = null;
        schedulerRegistered = false;

        DisposeTextures();
        isDisposed = true;
    }

    private void DisposeTextures()
    {
        textureStore.Dispose();
        texturesCreated = false;
        lastBlockAtlasReloadIteration = short.MinValue;
        lastBlockAtlasNonNullPositions = -1;
        lastScheduledAtlasReloadIteration = short.MinValue;
        lastScheduledAtlasNonNullPositions = -1;
        IsInitialized = false;
        IsBuildComplete = false;
    }

}
