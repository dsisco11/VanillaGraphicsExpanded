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

    private PbrMaterialAtlasTextures() { }

    public bool IsInitialized { get; private set; }

    public void Initialize(ICoreClientAPI capi)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));

        DisposeTextures();

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
        // The pixel builder is pure and can be unit-tested independently.
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

        foreach ((int atlasTexId, float[] pixels) in result.PixelBuffersByAtlasTexId)
        {
            (int width, int height) = result.SizesByAtlasTexId[atlasTexId];

            var materialParamsTex = DynamicTexture.CreateWithData(
                width,
                height,
                PixelInternalFormat.Rgb16f,
                pixels,
                TextureFilterMode.Nearest,
                debugName: $"vge_materialParams_atlas_{atlasTexId}");

            DynamicTexture? normalDepthTex = null;

            if (ConfigModSystem.Config.EnableNormalDepthAtlas)
            {
                // Placeholder normal+depth atlas page texture (filled later by GPU baker).
                // Must match atlas page dimensions 1:1 for UV alignment.
                normalDepthTex = DynamicTexture.Create(
                    width,
                    height,
                    PixelInternalFormat.Rgba16f,
                    TextureFilterMode.Nearest,
                    debugName: $"vge_normalDepth_atlas_{atlasTexId}");

                // Phase 3 plumbing: bake placeholder content on the GPU.
                PbrNormalDepthAtlasGpuBaker.BakePerTexture(
                    capi,
                    baseAlbedoAtlasPageTexId: atlasTexId,
                    destNormalDepthTexId: normalDepthTex.TextureId,
                    atlasWidth: width,
                    atlasHeight: height,
                    // Bake all rects in the block atlas page, not only those that have an explicit PBR material entry.
                    // Otherwise many valid albedo tiles remain at the clear color and appear "unprocessed" (e.g. gray).
                    texturePositions: atlas.Positions);
            }

            pageTexturesByAtlasTexId[atlasTexId] = new PbrMaterialAtlasPageTextures(materialParamsTex, normalDepthTex);
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
        IsInitialized = false;
    }

}
