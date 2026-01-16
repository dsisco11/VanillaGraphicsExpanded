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

    private bool texturesCreated;
    private short lastBlockAtlasReloadIteration = -1;

    private PbrMaterialAtlasTextures() { }

    public bool IsInitialized { get; private set; }
    public bool AreTexturesCreated => texturesCreated;

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

        // Guard: avoid repopulating for the same atlas generation.
        // The engine bumps reloadIteration for TextureAtlasPosition each time the block atlas is rebuilt.
        IBlockTextureAtlasAPI preAtlas = capi.BlockTextureAtlas;
        short currentReload = preAtlas.Positions != null && preAtlas.Positions.Length > 0
            ? preAtlas.Positions[0].reloadIteration
            : (short)-1;

        if (IsInitialized && currentReload >= 0 && currentReload == lastBlockAtlasReloadIteration)
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
            foreach ((int atlasTexId, int width, int height) in atlasPages)
            {
                if (!pageTexturesByAtlasTexId.TryGetValue(atlasTexId, out PbrMaterialAtlasPageTextures? pageTextures)
                    || pageTextures.NormalDepthTexture is null)
                {
                    continue;
                }

                // Gather a more exhaustive set of atlas rects to bake.
                var bakePositions = new List<TextureAtlasPosition>(capacity: atlas.Positions.Length + 1024);

                foreach (TextureAtlasPosition? pos in atlas.Positions)
                {
                    if (pos is not null)
                    {
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

                if (ConfigModSystem.Config.DebugLogNormalDepthAtlas)
                {
                    capi.Logger.Debug(
                        "[VGE] Normal+depth atlas bake input: pageTexId={0} positionsFromAtlasSubId={1} addedFromAssetScan={2}",
                        atlasTexId,
                        atlas.Positions.Length,
                        addedFromAssetScan);
                }
            }
        }

        capi.Logger.Notification(
            "[VGE] Built material param atlas textures: {0} atlas page(s), {1} texture rect(s) filled",
            pageTexturesByAtlasTexId.Count,
            result.FilledRects);

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
        }
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
        lastBlockAtlasReloadIteration = -1;
        IsInitialized = false;
    }

}
