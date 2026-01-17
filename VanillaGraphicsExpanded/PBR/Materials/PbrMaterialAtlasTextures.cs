using System;
using System.Collections.Generic;
using System.Linq;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.ModSystems;
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

    private readonly Dictionary<int, PbrMaterialAtlasPageTextures> pageTexturesByAtlasTexId = new();
    private bool isDisposed;

    private short lastBlockAtlasReloadIteration = short.MinValue;
    private int lastBlockAtlasNonNullPositions = -1;

    private bool texturesCreated;

    private PbrMaterialAtlasTextures() { }

    public bool IsInitialized { get; private set; }
    public bool AreTexturesCreated => texturesCreated;

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
            if (!pageTexturesByAtlasTexId.TryGetValue(atlasTexId, out PbrMaterialAtlasPageTextures? pageTextures)
                || pageTextures.NormalDepthTexture is null
                || !pageTextures.NormalDepthTexture.IsValid)
            {
                continue;
            }

            PbrNormalDepthAtlasGpuBaker.BakePerTexture(
                capi,
                baseAlbedoAtlasPageTexId: atlasTexId,
                destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
                atlasWidth: width,
                atlasHeight: height,
                texturePositions: atlas.Positions);
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
            return;
        }

        // If the atlas texture ids changed (atlas rebuild), drop the old textures and recreate.
        if (pageTexturesByAtlasTexId.Count > 0 && !atlasPages.All(p => pageTexturesByAtlasTexId.ContainsKey(p.atlasTextureId)))
        {
            DisposeTextures();
        }

        foreach ((int atlasTexId, int width, int height) in atlasPages)
        {
            if (pageTexturesByAtlasTexId.ContainsKey(atlasTexId))
            {
                continue;
            }

            // Initialize with defaults; contents get populated on BlockTexturesLoaded.
            float[] defaultParams = new float[width * height * 3];
            PbrMaterialParamsPixelBuilder.FillRgbTriplets(
                defaultParams,
                PbrMaterialParamsPixelBuilder.DefaultRoughness,
                PbrMaterialParamsPixelBuilder.DefaultMetallic,
                PbrMaterialParamsPixelBuilder.DefaultEmissive);

            var materialParamsTex = DynamicTexture.CreateWithData(
                width,
                height,
                PixelInternalFormat.Rgb16f,
                defaultParams,
                TextureFilterMode.Nearest,
                debugName: $"vge_materialParams_atlas_{atlasTexId}");

            DynamicTexture? normalDepthTex = null;
            if (ConfigModSystem.Config.EnableNormalDepthAtlas)
            {
                // Placeholder until PopulateAtlasContents runs the bake.
                normalDepthTex = DynamicTexture.Create(
                    width,
                    height,
                    PixelInternalFormat.Rgba16f,
                    TextureFilterMode.Nearest,
                    debugName: $"vge_normalDepth_atlas_{atlasTexId}");
            }

            pageTexturesByAtlasTexId[atlasTexId] = new PbrMaterialAtlasPageTextures(materialParamsTex, normalDepthTex);
        }

        texturesCreated = pageTexturesByAtlasTexId.Count > 0;
        IsInitialized = false;
    }

    /// <summary>
    /// Phase 2: compute and upload material params, then (optionally) bake normal+depth.
    /// Intended to run after the block atlas is finalized (e.g. on BlockTexturesLoaded).
    /// </summary>
    public void PopulateAtlasContents(ICoreClientAPI capi)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));

        // Guard: avoid repopulating when nothing changed.
        // Note: some mods may insert additional textures into the atlas after BlockTexturesLoaded.
        // In those cases reloadIteration may remain unchanged, so we also track the non-null position count.
        IBlockTextureAtlasAPI preAtlas = capi.BlockTextureAtlas;
        (short currentReload, int nonNullCount) = GetAtlasStats(preAtlas);

        if (IsInitialized &&
            currentReload >= 0 && currentReload == lastBlockAtlasReloadIteration &&
            nonNullCount == lastBlockAtlasNonNullPositions)
        {
            return;
        }

        CreateTextureObjects(capi);

        if (!texturesCreated)
        {
            return;
        }

        if (!PbrMaterialRegistry.Instance.IsInitialized)
        {
            capi.Logger.Warning("[VGE] Material atlas textures: registry not initialized; skipping.");
            IsInitialized = false;
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

            if (!PbrMaterialAtlasPositionResolver.TryResolve(TryGetAtlasPosition, texture, out TextureAtlasPosition? texPos)
                || texPos is null)
            {
                continue;
            }

            texturePositions[texture] = texPos;
            materialsByTexture[texture] = definition;
        }

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

            // Convert normalized atlas UV bounds to integer pixel bounds.
            int x1 = Math.Clamp((int)Math.Floor(texPos.x1 * atlasW), 0, atlasW - 1);
            int y1 = Math.Clamp((int)Math.Floor(texPos.y1 * atlasH), 0, atlasH - 1);
            int x2 = Math.Clamp((int)Math.Ceiling(texPos.x2 * atlasW), 0, atlasW);
            int y2 = Math.Clamp((int)Math.Ceiling(texPos.y2 * atlasH), 0, atlasH);

            int rectW = x2 - x1;
            int rectH = y2 - y1;
            if (rectW <= 0 || rectH <= 0)
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
                    expectedWidth: rectW,
                    expectedHeight: rectH))
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
                rectX: x1,
                rectY: y1,
                rectWidth: rectW,
                rectHeight: rectH,
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
            if (!pageTexturesByAtlasTexId.TryGetValue(atlasTexId, out PbrMaterialAtlasPageTextures? pageTextures))
            {
                continue;
            }

            (int width, int height) = result.SizesByAtlasTexId[atlasTexId];

            // Replace the material params texture (simple + safe). If you want a true in-place update later,
            // we can add a DynamicTexture.Update(...) method.
            pageTextures.MaterialParamsTexture.Dispose();
            var updated = DynamicTexture.CreateWithData(
                width,
                height,
                PixelInternalFormat.Rgb16f,
                pixels,
                TextureFilterMode.Nearest,
                debugName: $"vge_materialParams_atlas_{atlasTexId}");

            pageTexturesByAtlasTexId[atlasTexId] = new PbrMaterialAtlasPageTextures(updated, pageTextures.NormalDepthTexture);
        }

        if (ConfigModSystem.Config.EnableNormalDepthAtlas)
        {
            int totalNormalHeightOverridesApplied = 0;
            foreach ((int atlasTexId, int width, int height) in atlasPages)
            {
                if (!pageTexturesByAtlasTexId.TryGetValue(atlasTexId, out PbrMaterialAtlasPageTextures? pageTextures)
                    || pageTextures.NormalDepthTexture is null)
                {
                    continue;
                }

                // Preload/validate per-texture normalHeight overrides for this atlas page.
                // Only successfully-loaded overrides are excluded from the bake job list.
                var overrideRects = new Dictionary<(int x, int y, int w, int h), float[]>();
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

                    int x1 = Math.Clamp((int)Math.Floor(texPos.x1 * width), 0, width - 1);
                    int y1 = Math.Clamp((int)Math.Floor(texPos.y1 * height), 0, height - 1);
                    int x2 = Math.Clamp((int)Math.Ceiling(texPos.x2 * width), 0, width);
                    int y2 = Math.Clamp((int)Math.Ceiling(texPos.y2 * height), 0, height);

                    int rectW = x2 - x1;
                    int rectH = y2 - y1;
                    if (rectW <= 0 || rectH <= 0)
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
                            expectedWidth: rectW,
                            expectedHeight: rectH))
                    {
                        capi.Logger.Warning(
                            "[VGE] PBR override ignored: rule='{0}' target='{1}' override='{2}' reason='{3}'. Falling back to generated maps.",
                            overrides.RuleId ?? "(no id)",
                            targetTexture,
                            overrides.NormalHeight,
                            reason ?? "unknown error");
                        continue;
                    }

                    overrideRects[(x1, y1, rectW, rectH)] = floatRgba;
                }

                // Gather a more exhaustive set of atlas rects to bake.
                var bakePositions = new List<TextureAtlasPosition>(capacity: atlas.Positions.Length + 1024);

                int skippedByOverrides = 0;

                bool TryGetRectPx(TextureAtlasPosition pos, out (int x, int y, int w, int h) rect)
                {
                    int rx1 = Math.Clamp((int)Math.Floor(pos.x1 * width), 0, width - 1);
                    int ry1 = Math.Clamp((int)Math.Floor(pos.y1 * height), 0, height - 1);
                    int rx2 = Math.Clamp((int)Math.Ceiling(pos.x2 * width), 0, width);
                    int ry2 = Math.Clamp((int)Math.Ceiling(pos.y2 * height), 0, height);

                    int rw = rx2 - rx1;
                    int rh = ry2 - ry1;
                    rect = (rx1, ry1, rw, rh);
                    return rw > 0 && rh > 0;
                }

                foreach (TextureAtlasPosition? pos in atlas.Positions)
                {
                    if (pos is not null)
                    {
                        if (pos.atlasTextureId != atlasTexId)
                        {
                            continue;
                        }

                        if (overrideRects.Count > 0 && TryGetRectPx(pos, out (int x, int y, int w, int h) rect)
                            && overrideRects.ContainsKey(rect))
                        {
                            skippedByOverrides++;
                            continue;
                        }

                        bakePositions.Add(pos);
                    }
                }

                int addedFromAssetScan = 0;
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

                        if (overrideRects.Count > 0 && TryGetRectPx(pos, out (int x, int y, int w, int h) rect)
                            && overrideRects.ContainsKey(rect))
                        {
                            skippedByOverrides++;
                            continue;
                        }

                        bakePositions.Add(pos);
                        addedFromAssetScan++;
                    }
                }
                catch
                {
                    // Best-effort.
                }

                PbrNormalDepthAtlasGpuBaker.BakePerTexture(
                    capi,
                    baseAlbedoAtlasPageTexId: atlasTexId,
                    destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
                    atlasWidth: width,
                    atlasHeight: height,
                    texturePositions: bakePositions);

                // Apply explicit overrides after the bake clears and writes into the atlas.
                // (The baker always clears the whole atlas page sidecar for determinism.)
                int appliedOverrides = 0;
                foreach (((int x, int y, int w, int h) rect, float[] data) in overrideRects)
                {
                    pageTextures.NormalDepthTexture.UploadData(data, rect.x, rect.y, rect.w, rect.h);
                    appliedOverrides++;
                }

                totalNormalHeightOverridesApplied += appliedOverrides;

                if (ConfigModSystem.Config.DebugLogNormalDepthAtlas)
                {
                    capi.Logger.Debug(
                        "[VGE] Normal+depth atlas bake input: pageTexId={0} positionsFromAtlasSubId={1} addedFromAssetScan={2} skippedByOverrides={3} appliedOverrides={4}",
                        atlasTexId,
                        atlas.Positions.Length,
                        addedFromAssetScan,
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
            "[VGE] Built material param atlas textures: {0} atlas page(s), {1} texture rect(s) filled ({2} overridden)",
            pageTexturesByAtlasTexId.Count,
            result.FilledRects,
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
                pageTexturesByAtlasTexId.Count,
                string.Join(", ", sizeCounts.Select(kvp => $"{kvp.Key.Width}x{kvp.Key.Height}*{kvp.Value}")));
        }

        IsInitialized = pageTexturesByAtlasTexId.Count > 0;
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

    public bool TryGetMaterialParamsTextureId(int atlasTextureId, out int materialParamsTextureId)
    {
        if (pageTexturesByAtlasTexId.TryGetValue(atlasTextureId, out PbrMaterialAtlasPageTextures? pageTextures)
            && pageTextures.MaterialParamsTexture.IsValid)
        {
            materialParamsTextureId = pageTextures.MaterialParamsTexture.TextureId;
            return true;
        }

        materialParamsTextureId = 0;
        return false;
    }

    public bool TryGetNormalDepthTextureId(int atlasTextureId, out int normalDepthTextureId)
    {
        if (pageTexturesByAtlasTexId.TryGetValue(atlasTextureId, out PbrMaterialAtlasPageTextures? pageTextures)
            && pageTextures.NormalDepthTexture is not null
            && pageTextures.NormalDepthTexture.IsValid)
        {
            normalDepthTextureId = pageTextures.NormalDepthTexture.TextureId;
            return true;
        }

        normalDepthTextureId = 0;
        return false;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        DisposeTextures();
        isDisposed = true;
    }

    private void DisposeTextures()
    {
        foreach (PbrMaterialAtlasPageTextures pageTextures in pageTexturesByAtlasTexId.Values)
        {
            pageTextures.Dispose();
        }

        pageTexturesByAtlasTexId.Clear();
        texturesCreated = false;
        lastBlockAtlasReloadIteration = short.MinValue;
        lastBlockAtlasNonNullPositions = -1;
        IsInitialized = false;
    }

}
