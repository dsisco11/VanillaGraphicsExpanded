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

        // Pre-resolve atlas positions and material definitions once.
        var texturePositions = new Dictionary<AssetLocation, TextureAtlasPosition>(capacity: PbrMaterialRegistry.Instance.MaterialIdByTexture.Count);
        var materialsByTexture = new Dictionary<AssetLocation, PbrMaterialDefinition>(capacity: PbrMaterialRegistry.Instance.MaterialIdByTexture.Count);

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

        foreach ((AssetLocation texture, string _) in PbrMaterialRegistry.Instance.MaterialIdByTexture)
        {
            if (!PbrMaterialRegistry.Instance.TryGetMaterial(texture, out PbrMaterialDefinition definition))
            {
                continue;
            }

            if (PbrMaterialRegistry.Instance.ScaleByTexture.TryGetValue(texture, out PbrOverrideScale combinedScale))
            {
                definition = definition with { Scale = combinedScale };
            }

            if (!PbrMaterialAtlasPositionResolver.TryResolve(TryGetAtlasPosition, texture, out TextureAtlasPosition? texPos)
                || texPos is null)
            {
                continue;
            }

            texturePositions[texture] = texPos;
            materialsByTexture[texture] = definition;
        }

        int filledRects;
        int overriddenRects;
        if (ConfigModSystem.Config.EnableMaterialAtlasAsyncBuild)
        {
            filledRects = StartMaterialParamsAsyncBuild(
                atlasPages,
                texturePositions,
                materialsByTexture,
                currentReload,
                nonNullCount);
            overriddenRects = 0; // Phase 3 (override pipeline) will reintroduce these progressively.
        }
        else
        {
            (filledRects, overriddenRects) = PopulateMaterialParamsSync(
                capi,
                atlasPages,
                texturePositions,
                materialsByTexture);
        }

        if (ConfigModSystem.Config.EnableNormalDepthAtlas)
        {
            AtlasSnapshot snapshot = AtlasSnapshot.Capture(atlas);
            var planner = new MaterialAtlasNormalDepthBuildPlanner();
            var plan = planner.CreatePlan(
                snapshot,
                TryGetAtlasPosition,
                PbrMaterialRegistry.Instance,
                capi.Assets.GetLocations("textures/block/", domain: null));

            var runner = new MaterialAtlasNormalDepthBuildRunner(textureStore, new MaterialOverrideTextureLoader());
            (int bakedRects, int appliedOverrides) = runner.ExecutePlan(capi, plan);

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
                    plan.Pages.Count,
                    plan.PlanStats.BakeJobs,
                    plan.PlanStats.OverrideJobs,
                    plan.PlanStats.SkippedByOverrides);

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
        IReadOnlyDictionary<AssetLocation, TextureAtlasPosition> texturePositions,
        IReadOnlyDictionary<AssetLocation, PbrMaterialDefinition> materialsByTexture,
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

        var sizesByAtlasTexId = new Dictionary<int, (int width, int height)>(capacity: atlasPages.Count);
        foreach ((int atlasTextureId, int width, int height) in atlasPages)
        {
            sizesByAtlasTexId[atlasTextureId] = (width, height);
        }

        var tileJobs = new List<PbrMaterialAtlasTileJob>(capacity: materialsByTexture.Count);

        foreach ((AssetLocation texture, PbrMaterialDefinition definition) in materialsByTexture)
        {
            if (!texturePositions.TryGetValue(texture, out TextureAtlasPosition? texPos) || texPos is null)
            {
                continue;
            }

            if (!sizesByAtlasTexId.TryGetValue(texPos.atlasTextureId, out (int width, int height) size))
            {
                continue;
            }

            if (!AtlasRectResolver.TryResolvePixelRect(texPos, size.width, size.height, out AtlasRect rect))
            {
                continue;
            }

            AssetLocation? materialOverride = null;
            string? ruleId = null;
            AssetLocation? ruleSource = null;
            PbrOverrideScale scale = definition.Scale;

            if (PbrMaterialRegistry.Instance.OverridesByTexture.TryGetValue(texture, out PbrMaterialTextureOverrides overrides)
                && overrides.MaterialParams is not null)
            {
                materialOverride = overrides.MaterialParams;
                ruleId = overrides.RuleId;
                ruleSource = overrides.RuleSource;
                scale = overrides.Scale;
            }

            tileJobs.Add(new PbrMaterialAtlasTileJob(
                GenerationId: generationId,
                AtlasTextureId: texPos.atlasTextureId,
                RectX: rect.X,
                RectY: rect.Y,
                RectWidth: rect.Width,
                RectHeight: rect.Height,
                Texture: texture,
                Definition: definition,
                Priority: 0,
                MaterialParamsOverride: materialOverride,
                OverrideRuleId: ruleId,
                OverrideRuleSource: ruleSource,
                OverrideScale: scale));
        }

        var session = new PbrMaterialAtlasBuildSession(generationId, atlasPages, tileJobs);
        scheduler.StartSession(session);

        lastScheduledAtlasReloadIteration = currentReload;
        lastScheduledAtlasNonNullPositions = nonNullCount;
        IsBuildComplete = tileJobs.Count == 0;

        return tileJobs.Count;
    }

    private (int filledRects, int overriddenRects) PopulateMaterialParamsSync(
        ICoreClientAPI capi,
        IReadOnlyList<(int atlasTextureId, int width, int height)> atlasPages,
        IReadOnlyDictionary<AssetLocation, TextureAtlasPosition> texturePositions,
        IReadOnlyDictionary<AssetLocation, PbrMaterialDefinition> materialsByTexture)
    {
        var uploader = new MaterialAtlasParamsUploader(textureStore);
        var overrideLoader = new MaterialOverrideTextureLoader();

        var sizesByAtlasTexId = new Dictionary<int, (int width, int height)>(capacity: atlasPages.Count);
        foreach ((int atlasTextureId, int width, int height) in atlasPages)
        {
            if (atlasTextureId == 0 || width <= 0 || height <= 0)
            {
                continue;
            }

            sizesByAtlasTexId[atlasTextureId] = (width, height);
        }

        // Upload procedural tiles.
        int filledRects = 0;
        foreach ((AssetLocation texture, PbrMaterialDefinition definition) in materialsByTexture)
        {
            if (!texturePositions.TryGetValue(texture, out TextureAtlasPosition? texPos) || texPos is null)
            {
                continue;
            }

            if (!sizesByAtlasTexId.TryGetValue(texPos.atlasTextureId, out (int atlasW, int atlasH) size))
            {
                continue;
            }

            if (!AtlasRectResolver.TryResolvePixelRect(texPos, size.atlasW, size.atlasH, out AtlasRect rect))
            {
                continue;
            }

            float[] rgb = MaterialAtlasParamsBuilder.BuildRgb16fTile(
                texture,
                definition,
                rectWidth: rect.Width,
                rectHeight: rect.Height,
                CancellationToken.None);

            if (uploader.TryUploadTile(texPos.atlasTextureId, rect, rgb))
            {
                filledRects++;
            }
        }

        // Upload explicit material params overrides (if any) after base tiles.
        // Policy: if an override fails to load/validate, keep the procedural output.
        int overriddenRects = 0;
        foreach ((AssetLocation targetTexture, PbrMaterialTextureOverrides overrides) in PbrMaterialRegistry.Instance.OverridesByTexture)
        {
            if (overrides.MaterialParams is null)
            {
                continue;
            }

            if (!texturePositions.TryGetValue(targetTexture, out TextureAtlasPosition? texPos) || texPos is null)
            {
                continue;
            }

            if (!sizesByAtlasTexId.TryGetValue(texPos.atlasTextureId, out (int atlasW, int atlasH) size))
            {
                continue;
            }

            if (!AtlasRectResolver.TryResolvePixelRect(texPos, size.atlasW, size.atlasH, out AtlasRect rect))
            {
                continue;
            }

            if (!overrideLoader.TryLoadRgbaFloats01(
                    capi,
                    overrides.MaterialParams,
                    out int _,
                    out int _,
                    out float[] floatRgba01,
                    out string? reason,
                    expectedWidth: rect.Width,
                    expectedHeight: rect.Height))
            {
                capi.Logger.Warning(
                    "[VGE] PBR override ignored: rule='{0}' target='{1}' override='{2}' reason='{3}'. Falling back to generated maps.",
                    overrides.RuleId ?? "(no id)",
                    targetTexture,
                    overrides.MaterialParams,
                    reason ?? "unknown error");
                continue;
            }

            float[] rgb = new float[checked(rect.Width * rect.Height * 3)];
            MaterialAtlasParamsBuilder.ApplyOverrideToTileRgb16f(
                tileRgbTriplets: rgb,
                rectWidth: rect.Width,
                rectHeight: rect.Height,
                overrideRgba01: floatRgba01,
                scale: overrides.Scale);

            if (uploader.TryUploadTile(texPos.atlasTextureId, rect, rgb))
            {
                overriddenRects++;
            }
        }

        if (filledRects == 0 && materialsByTexture.Count > 0)
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
