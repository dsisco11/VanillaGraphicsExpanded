using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Cache.Artifacts;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR.Materials.Artifacts;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Profiling;
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

    private short lastWarmupAtlasReloadIteration = short.MinValue;
    private int lastWarmupAtlasNonNullPositions = -1;
    private readonly object schedulerLock = new();
    private ICoreClientAPI? capi;
    private int sessionGeneration;
    private bool buildRequested;

    private readonly MaterialAtlasArtifactBuildTracker artifactBuildTracker = new();
    private MaterialAtlasArtifactRenderQueue? artifactRenderQueue;
    private MaterialAtlasArtifactRenderQueueRenderer? artifactRenderQueueRenderer;
    private MaterialParamsAtlasArtifactGenerator? materialParamsArtifactGen;
    private NormalDepthAtlasArtifactGenerator? normalDepthArtifactGen;
    private bool artifactGeneratorsRegistered;

    private bool texturesCreated;

    private MaterialAtlasSystem() { }

    private void EnsureDiskCacheInitialized()
    {
        if (!ConfigModSystem.Config.MaterialAtlas.EnableCaching)
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

    internal bool TryGetArtifactBuildDiagnostics(out MaterialAtlasArtifactBuildDiagnostics diagnostics)
    {
        lock (schedulerLock)
        {
            if (!artifactGeneratorsRegistered || materialParamsArtifactGen is null)
            {
                diagnostics = default;
                return false;
            }

            ArtifactSchedulerStats mp = materialParamsArtifactGen.GetStatsSnapshot();
            ArtifactSchedulerStats nd = normalDepthArtifactGen?.GetStatsSnapshot() ?? default;

            diagnostics = new MaterialAtlasArtifactBuildDiagnostics(
                GenerationId: artifactBuildTracker.GenerationId,
                Remaining: artifactBuildTracker.Remaining,
                IsComplete: artifactBuildTracker.IsComplete,
                MaterialParams: mp,
                NormalDepth: nd);

            return diagnostics.GenerationId > 0;
        }
    }

    public bool IsInitialized { get; private set; }
    public bool AreTexturesCreated => texturesCreated;
    public bool IsBuildComplete { get; private set; }

    public void WarmupAtlasCache(ICoreClientAPI capi)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));

        // Warmup is intended to be cache-only and async; if either is disabled, it provides no value.
        if (!ConfigModSystem.Config.MaterialAtlas.EnableCaching || !ConfigModSystem.Config.MaterialAtlas.EnableAsync)
        {
            return;
        }

        this.capi = capi;
        EnsureSchedulerRegistered(capi);
        EnsureDiskCacheInitialized();

        CreateTextureObjects(capi);
        if (!texturesCreated)
        {
            return;
        }

        if (!PbrMaterialRegistry.Instance.IsInitialized)
        {
            return;
        }

        IBlockTextureAtlasAPI atlas = capi.BlockTextureAtlas;
        (short currentReload, int nonNullCount) = GetAtlasStats(atlas);

        if (currentReload >= 0
            && currentReload == lastWarmupAtlasReloadIteration
            && nonNullCount == lastWarmupAtlasNonNullPositions)
        {
            return;
        }

        // Build plans (same work as PopulateAtlasContents, but we will only schedule cache hits).
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
        var materialPlan = new MaterialAtlasBuildPlanner().CreatePlan(
            snapshot,
            TryGetAtlasPosition,
            PbrMaterialRegistry.Instance,
            blockTextureAssets: Array.Empty<AssetLocation>(),
            enableNormalDepth: false);

        MaterialAtlasNormalDepthBuildPlan? normalDepthPlan = null;
        if (ConfigModSystem.Config.MaterialAtlas.EnableNormalMaps)
        {
            snapshot = AtlasSnapshot.Capture(atlas);
            var ndPlanner = new MaterialAtlasNormalDepthBuildPlanner();
            normalDepthPlan = ndPlanner.CreatePlan(
                snapshot,
                TryGetAtlasPosition,
                PbrMaterialRegistry.Instance,
                capi.Assets.GetLocations("textures/block/", domain: null));
        }

        MaterialAtlasCacheKeyInputs cacheInputs = MaterialAtlasCacheKeyInputs.Create(ConfigModSystem.Config, snapshot, PbrMaterialRegistry.Instance);

        // Determine atlas pages for session page state.
        var atlasPages = new List<(int atlasTextureId, int width, int height)>(capacity: atlas.AtlasTextures.Count);
        foreach (LoadedTexture atlasPage in atlas.AtlasTextures)
        {
            if (atlasPage.TextureId == 0 || atlasPage.Width <= 0 || atlasPage.Height <= 0)
            {
                continue;
            }

            atlasPages.Add((atlasPage.TextureId, atlasPage.Width, atlasPage.Height));
        }

        int generationId = Interlocked.Increment(ref sessionGeneration);
        if (generationId <= 0)
        {
            sessionGeneration = 1;
            generationId = 1;
        }

        int totalMaterialCandidates;
        {
            var tileRects = new HashSet<(int atlasTexId, AtlasRect rect)>(capacity: materialPlan.MaterialParamsTiles.Count);
            foreach (var t in materialPlan.MaterialParamsTiles)
            {
                tileRects.Add((t.AtlasTextureId, t.Rect));
            }

            int overrideOnly = 0;
            foreach (var ov in materialPlan.MaterialParamsOverrides)
            {
                if (!tileRects.Contains((ov.AtlasTextureId, ov.Rect)))
                {
                    overrideOnly++;
                }
            }

            totalMaterialCandidates = materialPlan.MaterialParamsTiles.Count + overrideOnly;
        }

        int totalNormalDepthCandidates = normalDepthPlan is null
            ? 0
            : checked(normalDepthPlan.BakeJobs.Count + normalDepthPlan.OverrideJobs.Count);

        lastWarmupAtlasReloadIteration = currentReload;
        lastWarmupAtlasNonNullPositions = nonNullCount;

        // New pipeline: enqueue cache-hit uploads directly into the artifact generators.
        if (materialParamsArtifactGen is null)
        {
            return;
        }

        int enqueuedMaterial = 0;
        int enqueuedNormalDepth = 0;

        // Material params warmup: upload cached base and/or cached override tiles.
        foreach (AtlasBuildPlan.MaterialParamsTileJob tile in materialPlan.MaterialParamsTiles)
        {
            if (!textureStore.TryGetMaterialParamsTextureId(tile.AtlasTextureId, out int materialTexId) || materialTexId == 0)
            {
                continue;
            }

            AtlasCacheKey baseKey = cacheKeyBuilder.BuildMaterialParamsTileKey(
                cacheInputs,
                tile.AtlasTextureId,
                tile.Rect,
                tile.Texture,
                tile.Definition,
                tile.Scale);

            // Base tile.
            if (diskCache.HasMaterialParamsTile(baseKey))
            {
                var wk = new MaterialParamsAtlasArtifactGenerator.WorkKey(
                    GenerationId: generationId,
                    AtlasTextureId: tile.AtlasTextureId,
                    MaterialParamsTextureId: materialTexId,
                    Rect: tile.Rect,
                    TargetTexture: tile.Texture,
                    SourceTexture: null,
                    Definition: null,
                    DefinitionScale: default,
                    EnableCache: true,
                    HasOverride: false,
                    IsOverrideOnly: false,
                    OverrideTexture: null,
                    OverrideScale: default,
                    RuleId: null,
                    RuleSource: null,
                    BaseCacheKey: baseKey,
                    OverrideCacheKey: default,
                    Priority: tile.Priority);

                if (materialParamsArtifactGen.Enqueue(wk))
                {
                    enqueuedMaterial++;
                }
            }
        }

        foreach (AtlasBuildPlan.MaterialParamsOverrideJob ov in materialPlan.MaterialParamsOverrides)
        {
            if (!textureStore.TryGetMaterialParamsTextureId(ov.AtlasTextureId, out int materialTexId) || materialTexId == 0)
            {
                continue;
            }

            AtlasCacheKey overrideKey = cacheKeyBuilder.BuildMaterialParamsOverrideTileKey(
                cacheInputs,
                ov.AtlasTextureId,
                ov.Rect,
                ov.TargetTexture,
                ov.OverrideTexture,
                ov.Scale,
                ov.RuleId,
                ov.RuleSource);

            if (!diskCache.HasMaterialParamsTile(overrideKey))
            {
                continue;
            }

            // Override tile warmup: load cached post-override output, but don't attempt override asset loads.
            var wk = new MaterialParamsAtlasArtifactGenerator.WorkKey(
                GenerationId: generationId,
                AtlasTextureId: ov.AtlasTextureId,
                MaterialParamsTextureId: materialTexId,
                Rect: ov.Rect,
                TargetTexture: ov.TargetTexture,
                SourceTexture: null,
                Definition: null,
                DefinitionScale: default,
                EnableCache: true,
                HasOverride: true,
                IsOverrideOnly: true,
                OverrideTexture: null,
                OverrideScale: default,
                RuleId: null,
                RuleSource: null,
                BaseCacheKey: default,
                OverrideCacheKey: overrideKey,
                Priority: ov.Priority);

            if (materialParamsArtifactGen.Enqueue(wk))
            {
                enqueuedMaterial++;
            }
        }

        if (normalDepthPlan is not null && normalDepthArtifactGen is not null)
        {
            var pageSizes = new Dictionary<int, (int w, int h)>(capacity: normalDepthPlan.Pages.Count);
            foreach (var p in normalDepthPlan.Pages)
            {
                pageSizes[p.AtlasTextureId] = (p.Width, p.Height);
            }

            foreach (var job in normalDepthPlan.BakeJobs)
            {
                if (!pageSizes.TryGetValue(job.AtlasTextureId, out (int w, int h) size))
                {
                    continue;
                }

                if (!textureStore.TryGetNormalDepthTextureId(job.AtlasTextureId, out int ndTexId) || ndTexId == 0)
                {
                    continue;
                }

                AtlasCacheKey key = cacheKeyBuilder.BuildNormalDepthTileKey(
                    cacheInputs,
                    job.AtlasTextureId,
                    job.Rect,
                    job.SourceTexture,
                    job.NormalScale,
                    job.DepthScale);

                if (!diskCache.HasNormalDepthTile(key))
                {
                    continue;
                }

                var wk = new NormalDepthAtlasArtifactGenerator.WorkKey(
                    GenerationId: generationId,
                    AtlasTextureId: job.AtlasTextureId,
                    NormalDepthTextureId: ndTexId,
                    AtlasWidth: size.w,
                    AtlasHeight: size.h,
                    Rect: job.Rect,
                    Kind: NormalDepthAtlasArtifactGenerator.JobKind.UploadCached,
                    TargetTexture: job.SourceTexture,
                    OverrideTexture: null,
                    NormalScale: job.NormalScale,
                    DepthScale: job.DepthScale,
                    RuleId: null,
                    RuleSource: null,
                    EnableCache: true,
                    CacheKey: key,
                    Priority: job.Priority);

                if (normalDepthArtifactGen.Enqueue(wk))
                {
                    enqueuedNormalDepth++;
                }
            }

            foreach (var ov in normalDepthPlan.OverrideJobs)
            {
                if (!pageSizes.TryGetValue(ov.AtlasTextureId, out (int w, int h) size))
                {
                    continue;
                }

                if (!textureStore.TryGetNormalDepthTextureId(ov.AtlasTextureId, out int ndTexId) || ndTexId == 0)
                {
                    continue;
                }

                AtlasCacheKey key = cacheKeyBuilder.BuildNormalDepthOverrideTileKey(
                    cacheInputs,
                    ov.AtlasTextureId,
                    ov.Rect,
                    ov.TargetTexture,
                    ov.OverrideTexture,
                    ov.NormalScale,
                    ov.DepthScale,
                    ov.RuleId,
                    ov.RuleSource);

                if (!diskCache.HasNormalDepthTile(key))
                {
                    continue;
                }

                var wk = new NormalDepthAtlasArtifactGenerator.WorkKey(
                    GenerationId: generationId,
                    AtlasTextureId: ov.AtlasTextureId,
                    NormalDepthTextureId: ndTexId,
                    AtlasWidth: size.w,
                    AtlasHeight: size.h,
                    Rect: ov.Rect,
                    Kind: NormalDepthAtlasArtifactGenerator.JobKind.UploadCached,
                    TargetTexture: ov.TargetTexture,
                    OverrideTexture: null,
                    NormalScale: ov.NormalScale,
                    DepthScale: ov.DepthScale,
                    RuleId: null,
                    RuleSource: null,
                    EnableCache: true,
                    CacheKey: key,
                    Priority: ov.Priority);

                if (normalDepthArtifactGen.Enqueue(wk))
                {
                    enqueuedNormalDepth++;
                }
            }
        }

        if (enqueuedMaterial == 0 && enqueuedNormalDepth == 0)
        {
            capi.Logger.Debug(
                "[VGE] Material atlas disk cache warmup: no cached tiles found (material 0/{0}, normalDepth 0/{1})",
                totalMaterialCandidates,
                totalNormalDepthCandidates);
            return;
        }

        capi.Logger.Debug(
            "[VGE] Material atlas disk cache warmup enqueued: material {0}/{1} tiles, normalDepth {2}/{3} jobs",
            enqueuedMaterial,
            totalMaterialCandidates,
            enqueuedNormalDepth,
            totalNormalDepthCandidates);
    }

    public void RebakeNormalDepthAtlas(ICoreClientAPI capi)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));
        if (!ConfigModSystem.Config.MaterialAtlas.EnableNormalMaps)
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
        _ = runner.ExecutePlan(capi, plan, cacheInputs, enableCache: ConfigModSystem.Config.MaterialAtlas.EnableCaching);

        if (ConfigModSystem.Config.MaterialAtlas.DebugLogNormalDepthAtlas)
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

        if (textureStore.NeedsResync(atlasPages, ConfigModSystem.Config.MaterialAtlas.EnableNormalMaps))
        {
            CancelActiveBuildSession();
        }

        textureStore.SyncToAtlasPages(atlasPages, ConfigModSystem.Config.MaterialAtlas.EnableNormalMaps);

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
            if (currentReload == lastScheduledAtlasReloadIteration &&
                nonNullCount == lastScheduledAtlasNonNullPositions)
            {
                IsBuildComplete = !ConfigModSystem.Config.MaterialAtlas.EnableAsync || artifactBuildTracker.IsComplete;
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

        MaterialAtlasNormalDepthBuildPlan? normalDepthPlan = null;
        if (ConfigModSystem.Config.MaterialAtlas.EnableNormalMaps)
        {
            snapshot = AtlasSnapshot.Capture(atlas);
            var ndPlanner = new MaterialAtlasNormalDepthBuildPlanner();
            normalDepthPlan = ndPlanner.CreatePlan(
                snapshot,
                TryGetAtlasPosition,
                PbrMaterialRegistry.Instance,
                capi.Assets.GetLocations("textures/block/", domain: null));
        }

        int filledRects;
        int overriddenRects;
        MaterialAtlasCacheKeyInputs cacheInputs = MaterialAtlasCacheKeyInputs.Create(ConfigModSystem.Config, snapshot, PbrMaterialRegistry.Instance);

        if (ConfigModSystem.Config.MaterialAtlas.EnableAsync)
        {
            filledRects = StartMaterialParamsAsyncBuild(
                atlasPages,
                plan,
                cacheInputs,
                currentReload,
                nonNullCount,
                normalDepthPlan);
            overriddenRects = 0; // overrides are applied asynchronously on the render thread.
        }
        else
        {
            (filledRects, overriddenRects) = PopulateMaterialParamsSync(
                capi,
                plan,
                cacheInputs);
        }

        if (normalDepthPlan is not null && !ConfigModSystem.Config.MaterialAtlas.EnableAsync)
        {
            var runner = new MaterialAtlasNormalDepthBuildRunner(textureStore, new MaterialOverrideTextureLoader(), diskCache, cacheKeyBuilder);
            (int bakedRects, int appliedOverrides, int bakeCacheHits, int bakeCacheMisses, int overrideCacheHits, int overrideCacheMisses) = runner.ExecutePlan(
                capi,
                normalDepthPlan,
                cacheInputs,
                enableCache: ConfigModSystem.Config.MaterialAtlas.EnableCaching);

            if (appliedOverrides > 0)
            {
                capi.Logger.Notification(
                    "[VGE] Normal+height overrides applied: {0}",
                    appliedOverrides);
            }

            if (ConfigModSystem.Config.MaterialAtlas.DebugLogNormalDepthAtlas)
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

            if (ConfigModSystem.Config.MaterialAtlas.EnableCaching)
            {
                capi.Logger.Debug(
                    "[VGE] Material atlas disk cache (normal+depth): bake hits={0} misses={1}; override hits={2} misses={3}",
                    bakeCacheHits,
                    bakeCacheMisses,
                    overrideCacheHits,
                    overrideCacheMisses);

                if (ConfigModSystem.Config.MaterialAtlas.DebugLogMaterialAtlasDiskCache)
                {
                    MaterialAtlasDiskCacheStats stats = diskCache.GetStatsSnapshot();
                    capi.Logger.Debug(
                        "[VGE] Material atlas disk cache: entries={0} bytes={1} evicted={2} ({3} bytes) mat(h/m/s)={4}/{5}/{6} nd(h/m/s)={7}/{8}/{9}",
                        stats.TotalEntries,
                        stats.TotalBytes,
                        stats.EvictedEntries,
                        stats.EvictedBytes,
                        stats.MaterialParams.Hits,
                        stats.MaterialParams.Misses,
                        stats.MaterialParams.Stores,
                        stats.NormalDepth.Hits,
                        stats.NormalDepth.Misses,
                        stats.NormalDepth.Stores);
                }
            }
        }

        if (normalDepthPlan is not null && ConfigModSystem.Config.MaterialAtlas.EnableAsync && ConfigModSystem.Config.MaterialAtlas.DebugLogNormalDepthAtlas)
        {
            capi.Logger.Debug(
                "[VGE] Normal+depth atlas plan (async): pages={0} bakeJobs={1} overrideJobs={2} skippedByOverrides={3}",
                normalDepthPlan.Pages.Count,
                normalDepthPlan.PlanStats.BakeJobs,
                normalDepthPlan.PlanStats.OverrideJobs,
                normalDepthPlan.PlanStats.SkippedByOverrides);
        }

        capi.Logger.Notification(
            ConfigModSystem.Config.MaterialAtlas.EnableAsync
                ? "[VGE] Material params async build scheduled: {0} atlas page(s), {1} tile(s) queued ({2} overrides deferred)"
                : "[VGE] Built material param atlas textures: {0} atlas page(s), {1} texture rect(s) filled ({2} overridden)",
            textureStore.PageCount,
            filledRects,
            overriddenRects);

        if (ConfigModSystem.Config.MaterialAtlas.DebugLogNormalDepthAtlas)
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
                ConfigModSystem.Config.MaterialAtlas.EnableNormalMaps,
                textureStore.PageCount,
                string.Join(", ", sizeCounts.Select(kvp => $"{kvp.Key.Width}x{kvp.Key.Height}*{kvp.Value}")));
        }

        IsInitialized = textureStore.PageCount > 0;
        IsBuildComplete = !ConfigModSystem.Config.MaterialAtlas.EnableAsync || artifactBuildTracker.IsComplete;
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

        if (artifactGeneratorsRegistered && capi is not null && artifactRenderQueueRenderer is not null)
        {
            try
            {
                capi.Event.UnregisterRenderer(artifactRenderQueueRenderer, EnumRenderStage.Before);
            }
            catch
            {
                // Best-effort.
            }
        }

        materialParamsArtifactGen?.Stop();
        normalDepthArtifactGen?.Stop();
        materialParamsArtifactGen = null;
        normalDepthArtifactGen = null;
        artifactRenderQueueRenderer = null;
        artifactRenderQueue = null;
        artifactGeneratorsRegistered = false;
    }

    public void CancelActiveBuildSession()
    {
        lock (schedulerLock)
        {
            materialParamsArtifactGen?.BumpSession();
            normalDepthArtifactGen?.BumpSession();
            if (artifactBuildTracker.GenerationId > 0)
            {
                artifactBuildTracker.Begin(artifactBuildTracker.GenerationId, 0);
            }
            lastScheduledAtlasReloadIteration = short.MinValue;
            lastScheduledAtlasNonNullPositions = -1;
            IsBuildComplete = false;
        }
    }

    private void EnsureSchedulerRegistered(ICoreClientAPI capi)
    {
        lock (schedulerLock)
        {
            // Artifact-based generators (new async pipeline).
            if (!artifactGeneratorsRegistered)
            {
                artifactRenderQueue ??= new MaterialAtlasArtifactRenderQueue(TryGetPageTexturesByAtlasTexId);
                artifactRenderQueueRenderer ??= new MaterialAtlasArtifactRenderQueueRenderer(artifactRenderQueue);
                capi.Event.RegisterRenderer(artifactRenderQueueRenderer, EnumRenderStage.Before, "vge_pbr_material_atlas_artifacts_render");

                materialParamsArtifactGen ??= new MaterialParamsAtlasArtifactGenerator(
                    capi,
                    diskCache,
                    artifactBuildTracker,
                    maxConcurrency: 2,
                    ioCapacity: 1,
                    gpuCapacity: 2);

                normalDepthArtifactGen ??= new NormalDepthAtlasArtifactGenerator(
                    capi,
                    diskCache,
                    artifactRenderQueue,
                    artifactBuildTracker,
                    maxConcurrency: 1,
                    ioCapacity: 1,
                    gpuCapacity: 2);

                materialParamsArtifactGen.Start();
                normalDepthArtifactGen.Start();

                artifactGeneratorsRegistered = true;
            }
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
        int nonNullCount,
        MaterialAtlasNormalDepthBuildPlan? normalDepthPlan)
    {
        if (materialParamsArtifactGen is null)
        {
            return 0;
        }

        using var scheduleScope = Profiler.BeginScope("MaterialAtlas.AsyncSchedule", "PBR");

        int generationId = Interlocked.Increment(ref sessionGeneration);
        if (generationId <= 0)
        {
            sessionGeneration = 1;
            generationId = 1;
        }

        // Cancel any in-flight work and start a new artifact session.
        materialParamsArtifactGen.BumpSession();
        normalDepthArtifactGen?.BumpSession();

        var overridesByRect = new Dictionary<(int atlasTexId, AtlasRect rect), AtlasBuildPlan.MaterialParamsOverrideJob>(capacity: plan.MaterialParamsOverrides.Count);
        foreach (AtlasBuildPlan.MaterialParamsOverrideJob ov in plan.MaterialParamsOverrides)
        {
            overridesByRect[(ov.AtlasTextureId, ov.Rect)] = ov;
        }

        bool enableCache = ConfigModSystem.Config.MaterialAtlas.EnableCaching;

        int plannedMaterialItems = 0;
        int plannedNormalDepthItems = 0;

        // Estimate total work up-front so the tracker can drive IsBuildComplete reliably.
        plannedMaterialItems = plan.MaterialParamsTiles.Count;
        {
            var tileRectsTmp = new HashSet<(int atlasTexId, AtlasRect rect)>(capacity: plan.MaterialParamsTiles.Count);
            foreach (var t in plan.MaterialParamsTiles)
            {
                tileRectsTmp.Add((t.AtlasTextureId, t.Rect));
            }

            foreach (var ov in plan.MaterialParamsOverrides)
            {
                if (!tileRectsTmp.Contains((ov.AtlasTextureId, ov.Rect)))
                {
                    plannedMaterialItems++;
                }
            }
        }

        if (normalDepthPlan is not null)
        {
            plannedNormalDepthItems = checked(normalDepthPlan.BakeJobs.Count + normalDepthPlan.OverrideJobs.Count);
        }

        int plannedTotal = plannedMaterialItems + plannedNormalDepthItems;
        artifactBuildTracker.Begin(generationId, plannedTotal);

        var tileRects = new HashSet<(int atlasTexId, AtlasRect rect)>(capacity: plan.MaterialParamsTiles.Count);
        int scheduledMaterial = 0;

        foreach (AtlasBuildPlan.MaterialParamsTileJob tile in plan.MaterialParamsTiles)
        {
            tileRects.Add((tile.AtlasTextureId, tile.Rect));

            bool hasOverride = overridesByRect.TryGetValue((tile.AtlasTextureId, tile.Rect), out AtlasBuildPlan.MaterialParamsOverrideJob ov);

            AtlasCacheKey baseKey = enableCache
                ? cacheKeyBuilder.BuildMaterialParamsTileKey(
                    cacheInputs,
                    tile.AtlasTextureId,
                    tile.Rect,
                    tile.Texture,
                    tile.Definition,
                    tile.Scale)
                : default;

            AtlasCacheKey overrideKey = enableCache && hasOverride
                ? cacheKeyBuilder.BuildMaterialParamsOverrideTileKey(
                    cacheInputs,
                    tile.AtlasTextureId,
                    tile.Rect,
                    ov.TargetTexture,
                    ov.OverrideTexture,
                    ov.Scale,
                    ov.RuleId,
                    ov.RuleSource)
                : default;

            if (!textureStore.TryGetMaterialParamsTextureId(tile.AtlasTextureId, out int materialTexId) || materialTexId == 0)
            {
                artifactBuildTracker.CompleteOne(generationId);
                continue;
            }

            var workKey = new MaterialParamsAtlasArtifactGenerator.WorkKey(
                GenerationId: generationId,
                AtlasTextureId: tile.AtlasTextureId,
                MaterialParamsTextureId: materialTexId,
                Rect: tile.Rect,
                TargetTexture: tile.Texture,
                SourceTexture: tile.Texture,
                Definition: tile.Definition,
                DefinitionScale: tile.Scale,
                EnableCache: enableCache,
                HasOverride: hasOverride,
                IsOverrideOnly: false,
                OverrideTexture: hasOverride ? ov.OverrideTexture : null,
                OverrideScale: hasOverride ? ov.Scale : default,
                RuleId: hasOverride ? ov.RuleId : null,
                RuleSource: hasOverride ? ov.RuleSource : null,
                BaseCacheKey: baseKey,
                OverrideCacheKey: overrideKey,
                Priority: tile.Priority);

            if (!materialParamsArtifactGen.Enqueue(workKey))
            {
                artifactBuildTracker.CompleteOne(generationId);
                continue;
            }

            scheduledMaterial++;
        }

        // Add cache-aware jobs for override-only rects so cache hits can skip the override stage,
        // and cache misses can trigger the override upload without overwriting default params.
        foreach (AtlasBuildPlan.MaterialParamsOverrideJob ov in plan.MaterialParamsOverrides)
        {
            if (tileRects.Contains((ov.AtlasTextureId, ov.Rect)))
            {
                continue;
            }

            AtlasCacheKey overrideKey = enableCache
                ? cacheKeyBuilder.BuildMaterialParamsOverrideTileKey(
                    cacheInputs,
                    ov.AtlasTextureId,
                    ov.Rect,
                    ov.TargetTexture,
                    ov.OverrideTexture,
                    ov.Scale,
                    ov.RuleId,
                    ov.RuleSource)
                : default;

            if (!textureStore.TryGetMaterialParamsTextureId(ov.AtlasTextureId, out int materialTexId) || materialTexId == 0)
            {
                artifactBuildTracker.CompleteOne(generationId);
                continue;
            }

            var workKey = new MaterialParamsAtlasArtifactGenerator.WorkKey(
                GenerationId: generationId,
                AtlasTextureId: ov.AtlasTextureId,
                MaterialParamsTextureId: materialTexId,
                Rect: ov.Rect,
                TargetTexture: ov.TargetTexture,
                SourceTexture: null,
                Definition: null,
                DefinitionScale: default,
                EnableCache: enableCache,
                HasOverride: true,
                IsOverrideOnly: true,
                OverrideTexture: ov.OverrideTexture,
                OverrideScale: ov.Scale,
                RuleId: ov.RuleId,
                RuleSource: ov.RuleSource,
                BaseCacheKey: default,
                OverrideCacheKey: overrideKey,
                Priority: ov.Priority);

            if (!materialParamsArtifactGen.Enqueue(workKey))
            {
                artifactBuildTracker.CompleteOne(generationId);
                continue;
            }

            scheduledMaterial++;
        }

        int scheduledNormalDepth = 0;
        if (normalDepthPlan is not null && normalDepthArtifactGen is not null)
        {
            var pageSizes = new Dictionary<int, (int w, int h)>(capacity: normalDepthPlan.Pages.Count);
            foreach (var p in normalDepthPlan.Pages)
            {
                pageSizes[p.AtlasTextureId] = (p.Width, p.Height);
            }

            foreach (var job in normalDepthPlan.BakeJobs)
            {
                if (!pageSizes.TryGetValue(job.AtlasTextureId, out (int w, int h) size))
                {
                    artifactBuildTracker.CompleteOne(generationId);
                    continue;
                }

                if (!textureStore.TryGetNormalDepthTextureId(job.AtlasTextureId, out int ndTexId) || ndTexId == 0)
                {
                    artifactBuildTracker.CompleteOne(generationId);
                    continue;
                }

                AtlasCacheKey key = enableCache
                    ? cacheKeyBuilder.BuildNormalDepthTileKey(
                        cacheInputs,
                        job.AtlasTextureId,
                        job.Rect,
                        job.SourceTexture,
                        job.NormalScale,
                        job.DepthScale)
                    : default;

                var wk = new NormalDepthAtlasArtifactGenerator.WorkKey(
                    GenerationId: generationId,
                    AtlasTextureId: job.AtlasTextureId,
                    NormalDepthTextureId: ndTexId,
                    AtlasWidth: size.w,
                    AtlasHeight: size.h,
                    Rect: job.Rect,
                    Kind: NormalDepthAtlasArtifactGenerator.JobKind.Bake,
                    TargetTexture: job.SourceTexture,
                    OverrideTexture: null,
                    NormalScale: job.NormalScale,
                    DepthScale: job.DepthScale,
                    RuleId: null,
                    RuleSource: null,
                    EnableCache: enableCache,
                    CacheKey: key,
                    Priority: job.Priority);

                if (!normalDepthArtifactGen.Enqueue(wk))
                {
                    artifactBuildTracker.CompleteOne(generationId);
                    continue;
                }

                scheduledNormalDepth++;
            }

            foreach (var ov in normalDepthPlan.OverrideJobs)
            {
                if (!pageSizes.TryGetValue(ov.AtlasTextureId, out (int w, int h) size))
                {
                    artifactBuildTracker.CompleteOne(generationId);
                    continue;
                }

                if (!textureStore.TryGetNormalDepthTextureId(ov.AtlasTextureId, out int ndTexId) || ndTexId == 0)
                {
                    artifactBuildTracker.CompleteOne(generationId);
                    continue;
                }

                AtlasCacheKey key = enableCache
                    ? cacheKeyBuilder.BuildNormalDepthOverrideTileKey(
                        cacheInputs,
                        ov.AtlasTextureId,
                        ov.Rect,
                        ov.TargetTexture,
                        ov.OverrideTexture,
                        ov.NormalScale,
                        ov.DepthScale,
                        ov.RuleId,
                        ov.RuleSource)
                    : default;

                var wk = new NormalDepthAtlasArtifactGenerator.WorkKey(
                    GenerationId: generationId,
                    AtlasTextureId: ov.AtlasTextureId,
                    NormalDepthTextureId: ndTexId,
                    AtlasWidth: size.w,
                    AtlasHeight: size.h,
                    Rect: ov.Rect,
                    Kind: NormalDepthAtlasArtifactGenerator.JobKind.Override,
                    TargetTexture: ov.TargetTexture,
                    OverrideTexture: ov.OverrideTexture,
                    NormalScale: ov.NormalScale,
                    DepthScale: ov.DepthScale,
                    RuleId: ov.RuleId,
                    RuleSource: ov.RuleSource,
                    EnableCache: enableCache,
                    CacheKey: key,
                    Priority: ov.Priority);

                if (!normalDepthArtifactGen.Enqueue(wk))
                {
                    artifactBuildTracker.CompleteOne(generationId);
                    continue;
                }

                scheduledNormalDepth++;
            }
        }

        lastScheduledAtlasReloadIteration = currentReload;
        lastScheduledAtlasNonNullPositions = nonNullCount;
        IsBuildComplete = artifactBuildTracker.IsComplete;

        return scheduledMaterial;
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

        bool enableCache = ConfigModSystem.Config.MaterialAtlas.EnableCaching;
        int baseHits = 0;
        int baseMisses = 0;
        int overrideHits = 0;
        int overrideMisses = 0;

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
                    overrideHits++;
                    continue;
                }

                if (overridesByRect.ContainsKey((tile.AtlasTextureId, tile.Rect)))
                {
                    overrideMisses++;
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

                    baseHits++;
                    continue;
                }

                baseMisses++;
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

                    overrideHits++;
                    continue;
                }

                overrideMisses++;
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

        if (enableCache)
        {
            capi.Logger.Debug(
                "[VGE] Material atlas disk cache (material params): base hits={0} misses={1}; override hits={2} misses={3}",
                baseHits,
                baseMisses,
                overrideHits,
                overrideMisses);

            if (ConfigModSystem.Config.MaterialAtlas.DebugLogMaterialAtlasDiskCache)
            {
                MaterialAtlasDiskCacheStats stats = diskCache.GetStatsSnapshot();
                capi.Logger.Debug(
                    "[VGE] Material atlas disk cache: entries={0} bytes={1} evicted={2} ({3} bytes) mat(h/m/s)={4}/{5}/{6} nd(h/m/s)={7}/{8}/{9}",
                    stats.TotalEntries,
                    stats.TotalBytes,
                    stats.EvictedEntries,
                    stats.EvictedBytes,
                    stats.MaterialParams.Hits,
                    stats.MaterialParams.Misses,
                    stats.MaterialParams.Stores,
                    stats.NormalDepth.Hits,
                    stats.NormalDepth.Misses,
                    stats.NormalDepth.Stores);
            }
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
