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
            return;
        }

        // Bake using current atlas pixel contents. This is intentionally "force" and does not depend on reloadIteration,
        // because some textures are inserted/updated after BlockTexturesLoaded without changing atlas layout.
        foreach ((int atlasTexId, int width, int height) in atlasPages)
        {
            if (!textureStore.TryGetPageTextures(atlasTexId, out PbrMaterialAtlasPageTextures pageTextures)
                || pageTextures.NormalDepthTexture is null
                || !pageTextures.NormalDepthTexture.IsValid)
            {
                continue;
            }

            // Match the atlas-build path: clear once per page, then bake per rect with resolved scale.
            // BakePerTexture() cannot apply per-texture scales because atlas.Positions entries don't carry asset keys.
            PbrNormalDepthAtlasGpuBaker.ClearAtlasPage(
                capi,
                destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
                atlasWidth: width,
                atlasHeight: height);

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

            bool TryGetRectPx(TextureAtlasPosition pos, out AtlasRect rect)
                => AtlasRectResolver.TryResolvePixelRect(pos, width, height, out rect);

            var bakeRects = new Dictionary<AtlasRect, (float normalScale, float depthScale)>();

            // Seed from atlas positions with identity scale (no asset key).
            foreach (TextureAtlasPosition? pos in atlas.Positions)
            {
                if (pos is null || pos.atlasTextureId != atlasTexId)
                {
                    continue;
                }

                if (TryGetRectPx(pos, out AtlasRect rect))
                {
                    bakeRects.TryAdd(rect, (normalScale: 1f, depthScale: 1f));
                }
            }

            // Refine scales using registry mapping (asset scan) so defaults.scale.normal affects final normals.
            try
            {
                int sampledNonIdentity = 0;
                int sampledTotal = 0;
                const int MaxSamplesToLog = 12;

                foreach (AssetLocation tex in capi.Assets.GetLocations("textures/block/", domain: null))
                {
                    if (!PbrMaterialAtlasPositionResolver.TryResolve(TryGetAtlasPosition, tex, out TextureAtlasPosition? pos)
                        || pos is null
                        || pos.atlasTextureId != atlasTexId)
                    {
                        continue;
                    }

                    if (!TryGetRectPx(pos, out AtlasRect rect))
                    {
                        continue;
                    }

                    float ns = 1f;
                    float ds = 1f;
                    if (PbrMaterialRegistry.Instance.TryGetScale(tex, out PbrOverrideScale scale))
                    {
                        ns = scale.Normal;
                        ds = scale.Depth;
                    }

                    bakeRects[rect] = (normalScale: ns, depthScale: ds);

                    if (ConfigModSystem.Config.DebugLogNormalDepthAtlas && sampledTotal < MaxSamplesToLog)
                    {
                        bool nonIdentity = Math.Abs(ns - 1f) > 1e-6f || Math.Abs(ds - 1f) > 1e-6f;

                        // Prefer logging non-identity scales first (i.e., actually proving scaling is applied).
                        if (nonIdentity || sampledNonIdentity == 0)
                        {
                            capi.Logger.Debug(
                                "[VGE] Normal+depth rebake scale sample: pageTexId={0} tex={1} rect=({2},{3},{4},{5}) normalScale={6:0.###} depthScale={7:0.###}",
                                atlasTexId,
                                tex,
                                rect.X,
                                rect.Y,
                                rect.Width,
                                rect.Height,
                                ns,
                                ds);

                            sampledTotal++;
                            if (nonIdentity) sampledNonIdentity++;
                        }
                    }
                }

                if (ConfigModSystem.Config.DebugLogNormalDepthAtlas)
                {
                    capi.Logger.Debug(
                        "[VGE] Normal+depth rebake scale sampling complete: pageTexId={0} samplesLogged={1}",
                        atlasTexId,
                        sampledTotal);
                }
            }
            catch
            {
                // Best-effort.
            }

                foreach ((AtlasRect rect, (float normalScale, float depthScale) scale) in bakeRects)
            {
                _ = PbrNormalDepthAtlasGpuBaker.BakePerRect(
                    capi,
                    baseAlbedoAtlasPageTexId: atlasTexId,
                    destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
                    atlasWidth: width,
                    atlasHeight: height,
                    rectX: rect.X,
                    rectY: rect.Y,
                    rectWidth: rect.Width,
                    rectHeight: rect.Height,
                    normalScale: scale.normalScale,
                    depthScale: scale.depthScale);
            }
        }

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
            int totalNormalHeightOverridesApplied = 0;
            foreach ((int atlasTexId, int width, int height) in atlasPages)
            {
                if (!textureStore.TryGetPageTextures(atlasTexId, out PbrMaterialAtlasPageTextures pageTextures)
                    || pageTextures.NormalDepthTexture is null)
                {
                    continue;
                }

                // Preload/validate per-texture normalHeight overrides for this atlas page.
                // Only successfully-loaded overrides are excluded from the bake job list.
                var overrideRects = new Dictionary<AtlasRect, (float[] data, bool isRented)>();
                foreach ((AssetLocation targetTexture, PbrMaterialTextureOverrides overrides) in PbrMaterialRegistry.Instance.OverridesByTexture)
                {
                    if (overrides.NormalHeight is null)
                    {
                        continue;
                    }

                    if (!PbrMaterialAtlasPositionResolver.TryResolve(TryGetAtlasPosition, targetTexture, out TextureAtlasPosition? texPos)
                        || texPos is null
                        || texPos.atlasTextureId != atlasTexId)
                    {
                        continue;
                    }

                    if (!AtlasRectResolver.TryResolvePixelRect(texPos, width, height, out AtlasRect rect))
                    {
                        continue;
                    }

                    if (!PbrOverrideTextureLoader.TryLoadRgbaFloats01(
                            capi,
                            overrides.NormalHeight,
                            out int _,
                            out int _,
                            out float[] floatRgba,
                            out string? reason,
                            expectedWidth: rect.Width,
                            expectedHeight: rect.Height))
                    {
                        capi.Logger.Warning(
                            "[VGE] PBR override ignored: rule='{0}' target='{1}' override='{2}' reason='{3}'. Falling back to generated maps.",
                            overrides.RuleId ?? "(no id)",
                            targetTexture,
                            overrides.NormalHeight,
                            reason ?? "unknown error");
                        continue;
                    }

                    float normalScale = overrides.Scale.Normal;
                    float depthScale = overrides.Scale.Depth;
                    bool isIdentity = normalScale == 1f && depthScale == 1f;

                    if (isIdentity)
                    {
                        overrideRects[rect] = (floatRgba, isRented: false);
                        continue;
                    }

                    // Don't mutate cached float arrays from the override loader.
                    int floats = checked(rect.Width * rect.Height * 4);
                    float[] rented = ArrayPool<float>.Shared.Rent(floats);
                    Array.Copy(floatRgba, 0, rented, 0, floats);

                    // Channel packing must match vge_normaldepth.glsl (RGB = normalXYZ_01, A = height01).
                    SimdSpanMath.MultiplyClamp01Interleaved4InPlace2D(
                        destination4: rented.AsSpan(0, floats),
                        rectWidthPixels: rect.Width,
                        rectHeightPixels: rect.Height,
                        rowStridePixels: rect.Width,
                        mulRgb: normalScale,
                        mulA: depthScale);

                    overrideRects[rect] = (rented, isRented: true);
                }

                // Gather a more exhaustive set of atlas rects to bake.
                var bakeRects = new Dictionary<AtlasRect, (float normalScale, float depthScale)>(capacity: atlas.Positions.Length + 1024);

                int skippedByOverrides = 0;

                static bool TryGetRectPx(TextureAtlasPosition pos, int width, int height, out AtlasRect rect)
                    => AtlasRectResolver.TryResolvePixelRect(pos, width, height, out rect);

                foreach (TextureAtlasPosition? pos in atlas.Positions)
                {
                    if (pos is not null)
                    {
                        if (pos.atlasTextureId != atlasTexId)
                        {
                            continue;
                        }

                        if (overrideRects.Count > 0 && TryGetRectPx(pos, width, height, out AtlasRect rect)
                            && overrideRects.ContainsKey(rect))
                        {
                            skippedByOverrides++;
                            continue;
                        }

                        // No reliable asset key for atlas.Positions entries.
                        // Policy B: apply registry default scale to all rects, then overwrite where we can resolve a specific texture.
                        if (TryGetRectPx(pos, width, height, out AtlasRect rect2))
                        {
                            PbrOverrideScale def = PbrMaterialRegistry.Instance.DefaultScale;
                            bakeRects.TryAdd(rect2, (normalScale: def.Normal, depthScale: def.Depth));
                        }
                    }
                }

                int updatedFromScaleMap = 0;
                foreach ((AssetLocation tex, PbrOverrideScale scale) in PbrMaterialRegistry.Instance.ScaleByTexture)
                {
                    // Normal+depth bake is currently targeted at block textures.
                    if (!tex.Path.StartsWith("textures/block/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!PbrMaterialAtlasPositionResolver.TryResolve(TryGetAtlasPosition, tex, out TextureAtlasPosition? pos)
                        || pos is null
                        || pos.atlasTextureId != atlasTexId)
                    {
                        continue;
                    }

                    if (!TryGetRectPx(pos, width, height, out AtlasRect rect))
                    {
                        continue;
                    }

                    if (overrideRects.Count > 0 && overrideRects.ContainsKey(rect))
                    {
                        skippedByOverrides++;
                        continue;
                    }

                    if (bakeRects.TryGetValue(rect, out (float normalScale, float depthScale) existing))
                    {
                        if (Math.Abs(existing.normalScale - scale.Normal) > 1e-6f
                            || Math.Abs(existing.depthScale - scale.Depth) > 1e-6f)
                        {
                            bakeRects[rect] = (normalScale: scale.Normal, depthScale: scale.Depth);
                            updatedFromScaleMap++;
                        }
                    }
                    else
                    {
                        bakeRects[rect] = (normalScale: scale.Normal, depthScale: scale.Depth);
                        updatedFromScaleMap++;
                    }
                }

                int updatedFromAssetScan = 0;
                try
                {
                    foreach (AssetLocation tex in capi.Assets.GetLocations("textures/block/", domain: null))
                    {
                        if (!PbrMaterialAtlasPositionResolver.TryResolve(TryGetAtlasPosition, tex, out TextureAtlasPosition? pos)
                            || pos is null)
                        {
                            continue;
                        }

                        if (pos.atlasTextureId != atlasTexId)
                        {
                            continue;
                        }

                        if (overrideRects.Count > 0 && TryGetRectPx(pos, width, height, out AtlasRect rect)
                            && overrideRects.ContainsKey(rect))
                        {
                            skippedByOverrides++;
                            continue;
                        }

                        if (TryGetRectPx(pos, width, height, out AtlasRect rect2))
                        {
                            float ns = 1f;
                            float ds = 1f;
                            if (PbrMaterialRegistry.Instance.TryGetScale(tex, out PbrOverrideScale scale))
                            {
                                ns = scale.Normal;
                                ds = scale.Depth;
                            }

                            if (bakeRects.TryGetValue(rect2, out (float normalScale, float depthScale) existing))
                            {
                                if (Math.Abs(existing.normalScale - ns) > 1e-6f
                                    || Math.Abs(existing.depthScale - ds) > 1e-6f)
                                {
                                    bakeRects[rect2] = (normalScale: ns, depthScale: ds);
                                    updatedFromAssetScan++;
                                }
                            }
                            else
                            {
                                bakeRects[rect2] = (normalScale: ns, depthScale: ds);
                                updatedFromAssetScan++;
                            }
                        }
                    }
                }
                catch
                {
                    // Best-effort.
                }

                if (ConfigModSystem.Config.DebugLogNormalDepthAtlas)
                {
                    PbrOverrideScale def = PbrMaterialRegistry.Instance.DefaultScale;
                    capi.Logger.Debug(
                        "[VGE] Normal+depth atlas default scale: pageTexId={0} normal={1:0.###} depth={2:0.###}",
                        atlasTexId,
                        def.Normal,
                        def.Depth);

                    int logged = 0;
                    const int MaxSamplesToLog = 12;

                    foreach (var kvp in bakeRects)
                    {
                        if (logged >= MaxSamplesToLog)
                        {
                            break;
                        }

                        var rect = kvp.Key;
                        var scale = kvp.Value;
                        bool nonIdentity = Math.Abs(scale.normalScale - 1f) > 1e-6f || Math.Abs(scale.depthScale - 1f) > 1e-6f;
                        if (!nonIdentity)
                        {
                            continue;
                        }

                        capi.Logger.Debug(
                            "[VGE] Normal+depth atlas scale sample: pageTexId={0} rect=({1},{2},{3},{4}) normalScale={5:0.###} depthScale={6:0.###}",
                            atlasTexId,
                            rect.X,
                            rect.Y,
                            rect.Width,
                            rect.Height,
                            scale.normalScale,
                            scale.depthScale);

                        logged++;
                    }

                    capi.Logger.Debug(
                        "[VGE] Normal+depth atlas scale sampling complete: pageTexId={0} nonIdentitySamplesLogged={1}",
                        atlasTexId,
                        logged);
                }

                // Clear once per page, then bake per rect with scale.
                PbrNormalDepthAtlasGpuBaker.ClearAtlasPage(
                    capi,
                    destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
                    atlasWidth: width,
                    atlasHeight: height);

                int bakedRects = 0;
                foreach ((AtlasRect rect, (float normalScale, float depthScale) scale) in bakeRects)
                {
                    if (PbrNormalDepthAtlasGpuBaker.BakePerRect(
                        capi,
                        baseAlbedoAtlasPageTexId: atlasTexId,
                        destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
                        atlasWidth: width,
                        atlasHeight: height,
                        rectX: rect.X,
                        rectY: rect.Y,
                        rectWidth: rect.Width,
                        rectHeight: rect.Height,
                        normalScale: scale.normalScale,
                        depthScale: scale.depthScale))
                    {
                        bakedRects++;
                    }
                }

                // Apply explicit overrides after the bake clears and writes into the atlas.
                // (The baker always clears the whole atlas page sidecar for determinism.)
                int appliedOverrides = 0;
                foreach ((AtlasRect rect, (float[] data, bool isRented) ov) in overrideRects)
                {
                    try
                    {
                        pageTextures.NormalDepthTexture.UploadData(ov.data, rect.X, rect.Y, rect.Width, rect.Height);
                        appliedOverrides++;
                    }
                    finally
                    {
                        if (ov.isRented)
                        {
                            ArrayPool<float>.Shared.Return(ov.data);
                        }
                    }
                }

                totalNormalHeightOverridesApplied += appliedOverrides;

                if (ConfigModSystem.Config.DebugLogNormalDepthAtlas)
                {
                    capi.Logger.Debug(
                        "[VGE] Normal+depth atlas bake input: pageTexId={0} positionsFromAtlasSubId={1} updatedFromScaleMap={2} updatedFromAssetScan={3} bakedRects={4} skippedByOverrides={5} appliedOverrides={6}",
                        atlasTexId,
                        atlas.Positions.Length,
                        updatedFromScaleMap,
                        updatedFromAssetScan,
                        bakedRects,
                        skippedByOverrides,
                        appliedOverrides);
                }
            }

            if (totalNormalHeightOverridesApplied > 0)
            {
                capi.Logger.Notification(
                    "[VGE] Normal+height overrides applied: {0}",
                    totalNormalHeightOverridesApplied);
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
        var result = PbrMaterialParamsPixelBuilder.BuildRgb16fPixelBuffers(
            atlasPages,
            texturePositions,
            materialsByTexture);

        // Apply explicit material params overrides (if any) on top of the procedural buffers.
        // Policy: if an override fails to load/validate, keep the procedural output (warn once per target texture).
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

            if (!result.PixelBuffersByAtlasTexId.TryGetValue(texPos.atlasTextureId, out float[]? pixels))
            {
                continue;
            }

            (int atlasW, int atlasH) = result.SizesByAtlasTexId[texPos.atlasTextureId];

            if (!AtlasRectResolver.TryResolvePixelRect(texPos, atlasW, atlasH, out AtlasRect rect))
            {
                continue;
            }

            if (!PbrOverrideTextureLoader.TryLoadRgbaFloats01(
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

            // Channel packing must match vge_material.glsl (RGB = roughness, metallic, emissive).
            // Ignore alpha.
            PbrMaterialParamsOverrideApplier.ApplyRgbOverride(
                atlasRgbTriplets: pixels,
                atlasWidth: atlasW,
                atlasHeight: atlasH,
                rectX: rect.X,
                rectY: rect.Y,
                rectWidth: rect.Width,
                rectHeight: rect.Height,
                overrideRgba01: floatRgba01,
                scale: overrides.Scale);

            overriddenRects++;
        }

        if (result.FilledRects == 0 && materialsByTexture.Count > 0)
        {
            capi.Logger.Warning(
                "[VGE] Built material param atlas textures but filled 0 rects. This usually means atlas key mismatch (e.g. textures/...png vs block/...) or textures not in block atlas.");
        }

        // Upload CPU-built material params into the already-created textures.
        foreach ((int atlasTexId, float[] pixels) in result.PixelBuffersByAtlasTexId)
        {
            (int width, int height) = result.SizesByAtlasTexId[atlasTexId];

            // Replace the material params texture (simple + safe).
            textureStore.ReplaceMaterialParamsTextureWithData(atlasTexId, width, height, pixels);
        }

        return (result.FilledRects, overriddenRects);
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
