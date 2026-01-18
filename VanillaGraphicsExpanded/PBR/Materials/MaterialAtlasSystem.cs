using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR.Materials.Async;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Builds per-block-atlas GPU data textures that store base PBR material parameters per texel.
/// Sampled by patched vanilla chunk shaders using the same atlas UVs as <c>terrainTex</c>.
/// </summary>
internal sealed class MaterialAtlasSystem : IDisposable
{
    public static MaterialAtlasSystem Instance { get; } = new();

    private readonly MaterialAtlasTextureStore textureStore = new();
    private IMaterialAtlasDiskCache diskCache = MaterialAtlasDiskCacheNoOp.Instance;
    private readonly MaterialAtlasCacheKeyBuilder cacheKeyBuilder = new();
    private bool isDisposed;

    private short lastBlockAtlasReloadIteration = short.MinValue;
    private int lastBlockAtlasNonNullPositions = -1;
    private short lastScheduledAtlasReloadIteration = short.MinValue;
    private int lastScheduledAtlasNonNullPositions = -1;
    private readonly object schedulerLock = new();
    private ICoreClientAPI? capi;
    private MaterialAtlasBuildScheduler? scheduler;
    private MaterialAtlasBuildSchedulerRenderer? schedulerRenderer;
    private bool schedulerRegistered;
    private int sessionGeneration;
    private bool buildRequested;

    private bool texturesCreated;

    private MaterialAtlasSystem() { }

    private void EnsureDiskCacheInitialized()
    {
        if (!ConfigModSystem.Config.EnableMaterialAtlasDiskCache)
        {
            diskCache = MaterialAtlasDiskCacheNoOp.Instance;
            return;
        }

        if (diskCache is MaterialAtlasDiskCache)
        {
            return;
        }

        diskCache = MaterialAtlasDiskCache.CreateDefault();
    }

    internal MaterialAtlasTextureStore TextureStore => textureStore;

    internal bool TryGetAsyncBuildDiagnostics(out MaterialAtlasAsyncBuildDiagnostics diagnostics)
    {
        lock (schedulerLock)
        {
            if (scheduler is null)
            {
                diagnostics = default;
                return false;
            }

            diagnostics = scheduler.GetDiagnosticsSnapshot();
            return true;
        }
    }

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

        EnsureDiskCacheInitialized();

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

        MaterialAtlasCacheKeyInputs cacheInputs = MaterialAtlasCacheKeyInputs.Create(ConfigModSystem.Config, snapshot, PbrMaterialRegistry.Instance);

        var runner = new MaterialAtlasNormalDepthBuildRunner(textureStore, new MaterialOverrideTextureLoader(), diskCache, cacheKeyBuilder);
        _ = runner.ExecutePlan(capi, plan, cacheInputs, enableCache: ConfigModSystem.Config.EnableMaterialAtlasDiskCache);

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
        EnsureDiskCacheInitialized();

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
        EnsureDiskCacheInitialized();

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
        MaterialAtlasCacheKeyInputs cacheInputs = MaterialAtlasCacheKeyInputs.Create(ConfigModSystem.Config, snapshot, PbrMaterialRegistry.Instance);

        if (ConfigModSystem.Config.EnableMaterialAtlasAsyncBuild)
        {
            filledRects = StartMaterialParamsAsyncBuild(
                atlasPages,
                plan,
                cacheInputs,
                currentReload,
                nonNullCount);
            overriddenRects = 0; // overrides are applied asynchronously on the render thread.
        }
        else
        {
            (filledRects, overriddenRects) = PopulateMaterialParamsSync(
                capi,
                plan,
                cacheInputs);
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

            var runner = new MaterialAtlasNormalDepthBuildRunner(textureStore, new MaterialOverrideTextureLoader(), diskCache, cacheKeyBuilder);
            (int bakedRects, int appliedOverrides, int cacheHits, int cacheMisses) = runner.ExecutePlan(
                capi,
                normalDepthPlan,
                cacheInputs,
                enableCache: ConfigModSystem.Config.EnableMaterialAtlasDiskCache);

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

            if (ConfigModSystem.Config.EnableMaterialAtlasDiskCache && ConfigModSystem.Config.DebugLogMaterialAtlasDiskCache)
            {
                capi.Logger.Debug(
                    "[VGE] Material atlas disk cache (normal+depth): hits={0} misses={1}",
                    cacheHits,
                    cacheMisses);
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
            scheduler ??= new MaterialAtlasBuildScheduler();
            scheduler.Initialize(capi, TryGetPageTexturesByAtlasTexId);

            if (schedulerRegistered)
            {
                return;
            }

            schedulerRenderer ??= new MaterialAtlasBuildSchedulerRenderer(scheduler);
            capi.Event.RegisterRenderer(schedulerRenderer, EnumRenderStage.Before, "vge_pbr_material_atlas_async_build");
            schedulerRegistered = true;
        }
    }

    private MaterialAtlasPageTextures? TryGetPageTexturesByAtlasTexId(int atlasTextureId)
        => textureStore.TryGetPageTextures(atlasTextureId, out MaterialAtlasPageTextures pageTextures)
            ? pageTextures
            : null;

    private int StartMaterialParamsAsyncBuild(
        IReadOnlyList<(int atlasTextureId, int width, int height)> atlasPages,
        AtlasBuildPlan plan,
        MaterialAtlasCacheKeyInputs cacheInputs,
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

        var cpuJobs = new List<IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>>(capacity: plan.MaterialParamsTiles.Count);

        var overridesByRect = new Dictionary<(int atlasTexId, AtlasRect rect), AtlasBuildPlan.MaterialParamsOverrideJob>(capacity: plan.MaterialParamsOverrides.Count);
        foreach (AtlasBuildPlan.MaterialParamsOverrideJob ov in plan.MaterialParamsOverrides)
        {
            overridesByRect[(ov.AtlasTextureId, ov.Rect)] = ov;
        }

        bool enableCache = ConfigModSystem.Config.EnableMaterialAtlasDiskCache;
        int cacheHits = 0;
        int cacheMisses = 0;

        foreach (AtlasBuildPlan.MaterialParamsTileJob tile in plan.MaterialParamsTiles)
        {
            AtlasCacheKey key = default;
            if (enableCache)
            {
                // If a post-override tile is cached, we can skip both the procedural build and the override stage.
                if (overridesByRect.TryGetValue((tile.AtlasTextureId, tile.Rect), out AtlasBuildPlan.MaterialParamsOverrideJob ov)
                    && diskCache.TryLoadMaterialParamsTile(
                        cacheKeyBuilder.BuildMaterialParamsOverrideTileKey(
                            cacheInputs,
                            tile.AtlasTextureId,
                            tile.Rect,
                            ov.TargetTexture,
                            ov.OverrideTexture,
                            ov.Scale,
                            ov.RuleId,
                            ov.RuleSource),
                        out float[] cachedOverrideRgb))
                {
                    cpuJobs.Add(new MaterialAtlasParamsCpuCachedTileJob(
                        GenerationId: generationId,
                        AtlasTextureId: tile.AtlasTextureId,
                        Rect: tile.Rect,
                        RgbTriplets: cachedOverrideRgb,
                        TargetTexture: tile.Texture,
                        Priority: tile.Priority));

                    // Mark consumed so we don't enqueue the override upload for this rect.
                    overridesByRect.Remove((tile.AtlasTextureId, tile.Rect));
                    cacheHits++;
                    continue;
                }

                key = cacheKeyBuilder.BuildMaterialParamsTileKey(
                    cacheInputs,
                    tile.AtlasTextureId,
                    tile.Rect,
                    tile.Texture,
                    tile.Definition,
                    tile.Scale);

                if (diskCache.TryLoadMaterialParamsTile(key, out float[] rgb))
                {
                    cpuJobs.Add(new MaterialAtlasParamsCpuCachedTileJob(
                        GenerationId: generationId,
                        AtlasTextureId: tile.AtlasTextureId,
                        Rect: tile.Rect,
                        RgbTriplets: rgb,
                        TargetTexture: tile.Texture,
                        Priority: tile.Priority));
                    cacheHits++;
                    continue;
                }

                cacheMisses++;
            }

            cpuJobs.Add(new MaterialAtlasParamsCpuTileJob(
                GenerationId: generationId,
                AtlasTextureId: tile.AtlasTextureId,
                Rect: tile.Rect,
                Texture: tile.Texture,
                Definition: tile.Definition,
                Priority: tile.Priority,
                DiskCache: enableCache ? diskCache : null,
                CacheKey: key));
        }

        var overrideJobs = new List<MaterialAtlasParamsGpuOverrideUpload>(capacity: plan.MaterialParamsOverrides.Count);
        foreach (AtlasBuildPlan.MaterialParamsOverrideJob ov in plan.MaterialParamsOverrides)
        {
            // If we already satisfied this rect via cached post-override output, skip scheduling.
            if (!overridesByRect.ContainsKey((ov.AtlasTextureId, ov.Rect)))
            {
                continue;
            }

            // If an override-only rect is cached (or a rect with a missing tile job), upload directly and skip the override stage.
            if (enableCache)
            {
                AtlasCacheKey overrideKey = cacheKeyBuilder.BuildMaterialParamsOverrideTileKey(
                    cacheInputs,
                    ov.AtlasTextureId,
                    ov.Rect,
                    ov.TargetTexture,
                    ov.OverrideTexture,
                    ov.Scale,
                    ov.RuleId,
                    ov.RuleSource);

                if (diskCache.TryLoadMaterialParamsTile(overrideKey, out float[] cachedOverrideRgb))
                {
                    cpuJobs.Add(new MaterialAtlasParamsCpuCachedTileJob(
                        GenerationId: generationId,
                        AtlasTextureId: ov.AtlasTextureId,
                        Rect: ov.Rect,
                        RgbTriplets: cachedOverrideRgb,
                        TargetTexture: ov.TargetTexture,
                        Priority: ov.Priority));

                    cacheHits++;
                    continue;
                }
            }

            overrideJobs.Add(new MaterialAtlasParamsGpuOverrideUpload(
                GenerationId: generationId,
                AtlasTextureId: ov.AtlasTextureId,
                Rect: ov.Rect,
                TargetTexture: ov.TargetTexture,
                OverrideAsset: ov.OverrideTexture,
                RuleId: ov.RuleId,
                RuleSource: ov.RuleSource,
                Scale: ov.Scale,
                DiskCache: enableCache ? diskCache : null,
                CacheKey: enableCache
                    ? cacheKeyBuilder.BuildMaterialParamsOverrideTileKey(
                        cacheInputs,
                        ov.AtlasTextureId,
                        ov.Rect,
                        ov.TargetTexture,
                        ov.OverrideTexture,
                        ov.Scale,
                        ov.RuleId,
                        ov.RuleSource)
                    : default,
                Priority: ov.Priority));
        }

        var session = new MaterialAtlasBuildSession(
            generationId,
            atlasPages,
            cpuJobs,
            overrideJobs,
            new MaterialOverrideTextureLoader());
        scheduler.StartSession(session);

        if (enableCache && ConfigModSystem.Config.DebugLogMaterialAtlasDiskCache && capi is not null)
        {
            capi.Logger.Debug(
                "[VGE] Material atlas disk cache (material params): hits={0} misses={1}",
                cacheHits,
                cacheMisses);
        }

        lastScheduledAtlasReloadIteration = currentReload;
        lastScheduledAtlasNonNullPositions = nonNullCount;
        IsBuildComplete = cpuJobs.Count == 0 && overrideJobs.Count == 0;

        return cpuJobs.Count;
    }

    private (int filledRects, int overriddenRects) PopulateMaterialParamsSync(
        ICoreClientAPI capi,
        AtlasBuildPlan plan,
        MaterialAtlasCacheKeyInputs cacheInputs)
    {
        var uploader = new MaterialAtlasParamsUploader(textureStore);
        var overrideLoader = new MaterialOverrideTextureLoader();

        var overridesByRect = new Dictionary<(int atlasTexId, AtlasRect rect), AtlasBuildPlan.MaterialParamsOverrideJob>(capacity: plan.MaterialParamsOverrides.Count);
        foreach (AtlasBuildPlan.MaterialParamsOverrideJob ov in plan.MaterialParamsOverrides)
        {
            overridesByRect[(ov.AtlasTextureId, ov.Rect)] = ov;
        }

        var overriddenRectsByCache = new HashSet<(int atlasTexId, AtlasRect rect)>();

        bool enableCache = ConfigModSystem.Config.EnableMaterialAtlasDiskCache;
        int cacheHits = 0;
        int cacheMisses = 0;

        // Upload procedural tiles.
        int filledRects = 0;
        foreach (AtlasBuildPlan.MaterialParamsTileJob tile in plan.MaterialParamsTiles)
        {
            AtlasCacheKey key = default;

            if (enableCache)
            {
                // Prefer cached post-override output so we can skip both the procedural build and the override stage.
                if (overridesByRect.TryGetValue((tile.AtlasTextureId, tile.Rect), out AtlasBuildPlan.MaterialParamsOverrideJob ov)
                    && diskCache.TryLoadMaterialParamsTile(
                        cacheKeyBuilder.BuildMaterialParamsOverrideTileKey(
                            cacheInputs,
                            tile.AtlasTextureId,
                            tile.Rect,
                            ov.TargetTexture,
                            ov.OverrideTexture,
                            ov.Scale,
                            ov.RuleId,
                            ov.RuleSource),
                        out float[] cachedOverrideRgb)
                    && uploader.TryUploadTile(tile.AtlasTextureId, tile.Rect, cachedOverrideRgb))
                {
                    overriddenRectsByCache.Add((tile.AtlasTextureId, tile.Rect));
                    cacheHits++;
                    continue;
                }

                key = cacheKeyBuilder.BuildMaterialParamsTileKey(
                    cacheInputs,
                    tile.AtlasTextureId,
                    tile.Rect,
                    tile.Texture,
                    tile.Definition,
                    tile.Scale);

                if (diskCache.TryLoadMaterialParamsTile(key, out float[] cached))
                {
                    if (uploader.TryUploadTile(tile.AtlasTextureId, tile.Rect, cached))
                    {
                        filledRects++;
                    }

                    cacheHits++;
                    continue;
                }

                cacheMisses++;
            }

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

            if (enableCache)
            {
                diskCache.StoreMaterialParamsTile(key, tile.Rect.Width, tile.Rect.Height, rgb);
            }
        }

        // Upload explicit material params overrides (if any) after base tiles.
        // Policy: if an override fails to load/validate, keep the procedural output.
        int overriddenRects = overriddenRectsByCache.Count;
        foreach (AtlasBuildPlan.MaterialParamsOverrideJob ov in plan.MaterialParamsOverrides)
        {
            if (overriddenRectsByCache.Contains((ov.AtlasTextureId, ov.Rect)))
            {
                continue;
            }

            AtlasCacheKey overrideKey = default;
            if (enableCache)
            {
                overrideKey = cacheKeyBuilder.BuildMaterialParamsOverrideTileKey(
                    cacheInputs,
                    ov.AtlasTextureId,
                    ov.Rect,
                    ov.TargetTexture,
                    ov.OverrideTexture,
                    ov.Scale,
                    ov.RuleId,
                    ov.RuleSource);

                if (diskCache.TryLoadMaterialParamsTile(overrideKey, out float[] cachedOverrideRgb))
                {
                    if (uploader.TryUploadTile(ov.AtlasTextureId, ov.Rect, cachedOverrideRgb))
                    {
                        overriddenRects++;
                    }

                    cacheHits++;
                    continue;
                }

                cacheMisses++;
            }

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

            if (enableCache)
            {
                diskCache.StoreMaterialParamsTile(overrideKey, ov.Rect.Width, ov.Rect.Height, rgb);
            }
        }

        if (filledRects == 0 && PbrMaterialRegistry.Instance.MaterialIdByTexture.Count > 0)
        {
            capi.Logger.Warning(
                "[VGE] Built material param atlas textures but filled 0 rects. This usually means atlas key mismatch (e.g. textures/...png vs block/...) or textures not in block atlas.");
        }

        if (enableCache && ConfigModSystem.Config.DebugLogMaterialAtlasDiskCache)
        {
            capi.Logger.Debug(
                "[VGE] Material atlas disk cache (material params): hits={0} misses={1}",
                cacheHits,
                cacheMisses);
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
        try
        {
            diskCache.Clear();
        }
        catch
        {
            // Best-effort.
        }
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
