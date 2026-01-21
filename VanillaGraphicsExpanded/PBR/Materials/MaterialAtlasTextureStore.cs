using System;
using System.Collections.Generic;
using System.Linq;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Owns per-atlas-page <see cref="Texture2D"/> objects for the material params and normal+depth atlases.
/// Centralizes allocation, placeholder/default fills, and disposal so build/execution code can avoid managing
/// texture lifetimes directly.
/// </summary>
internal sealed class MaterialAtlasTextureStore : IDisposable
{
    private sealed record class PageEntry(int Width, int Height, MaterialAtlasPageTextures Textures);

    private readonly Dictionary<int, PageEntry> pagesByAtlasTexId = new();

    public int PageCount => pagesByAtlasTexId.Count;

    public bool HasAnyTextures => pagesByAtlasTexId.Count > 0;

    public bool TryGetPageTextures(int atlasTextureId, out MaterialAtlasPageTextures pageTextures)
    {
        if (pagesByAtlasTexId.TryGetValue(atlasTextureId, out PageEntry? entry))
        {
            pageTextures = entry.Textures;
            return true;
        }

        pageTextures = null!;
        return false;
    }

    public bool NeedsResync(IReadOnlyList<(int atlasTextureId, int width, int height)> atlasPages, bool enableNormalDepth)
    {
        ArgumentNullException.ThrowIfNull(atlasPages);

        if (pagesByAtlasTexId.Count == 0)
        {
            return true;
        }

        var desiredIds = new HashSet<int>(atlasPages.Select(p => p.atlasTextureId));

        foreach (int existingId in pagesByAtlasTexId.Keys)
        {
            if (!desiredIds.Contains(existingId))
            {
                return true;
            }
        }

        foreach ((int atlasTexId, int width, int height) in atlasPages)
        {
            if (atlasTexId == 0 || width <= 0 || height <= 0)
            {
                continue;
            }

            if (!pagesByAtlasTexId.TryGetValue(atlasTexId, out PageEntry? existing))
            {
                return true;
            }

            bool sizeChanged = existing.Width != width || existing.Height != height;
            bool normalDepthMismatch = enableNormalDepth
                ? existing.Textures.NormalDepthTexture is null
                : existing.Textures.NormalDepthTexture is not null;

            if (sizeChanged || normalDepthMismatch)
            {
                return true;
            }
        }

        return false;
    }

    public void SyncToAtlasPages(IReadOnlyList<(int atlasTextureId, int width, int height)> atlasPages, bool enableNormalDepth)
    {
        ArgumentNullException.ThrowIfNull(atlasPages);

        var desiredIds = new HashSet<int>(atlasPages.Select(p => p.atlasTextureId));

        // Drop pages that no longer exist.
        foreach (int existingId in pagesByAtlasTexId.Keys.ToArray())
        {
            if (!desiredIds.Contains(existingId))
            {
                pagesByAtlasTexId[existingId].Textures.Dispose();
                pagesByAtlasTexId.Remove(existingId);
            }
        }

        // Create/update pages.
        foreach ((int atlasTexId, int width, int height) in atlasPages)
        {
            if (atlasTexId == 0 || width <= 0 || height <= 0)
            {
                continue;
            }

            if (pagesByAtlasTexId.TryGetValue(atlasTexId, out PageEntry? existing))
            {
                bool sizeChanged = existing.Width != width || existing.Height != height;
                bool normalDepthMismatch = enableNormalDepth
                    ? existing.Textures.NormalDepthTexture is null
                    : existing.Textures.NormalDepthTexture is not null;

                if (!sizeChanged && !normalDepthMismatch)
                {
                    continue;
                }

                existing.Textures.Dispose();
                pagesByAtlasTexId.Remove(atlasTexId);
            }

            pagesByAtlasTexId[atlasTexId] = new PageEntry(width, height, CreatePageTextures(atlasTexId, width, height, enableNormalDepth));
        }
    }

    public bool TryGetMaterialParamsTextureId(int atlasTextureId, out int materialParamsTextureId)
    {
        if (TryGetPageTextures(atlasTextureId, out MaterialAtlasPageTextures pageTextures)
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
        if (TryGetPageTextures(atlasTextureId, out MaterialAtlasPageTextures pageTextures)
            && pageTextures.NormalDepthTexture is not null
            && pageTextures.NormalDepthTexture.IsValid)
        {
            normalDepthTextureId = pageTextures.NormalDepthTexture.TextureId;
            return true;
        }

        normalDepthTextureId = 0;
        return false;
    }

    public void ReplaceMaterialParamsTextureWithData(int atlasTextureId, int width, int height, float[] rgbTriplets)
    {
        ArgumentNullException.ThrowIfNull(rgbTriplets);

        if (!pagesByAtlasTexId.TryGetValue(atlasTextureId, out PageEntry? entry))
        {
            return;
        }

        Texture2D? existingNormalDepth = entry.Textures.NormalDepthTexture;

        entry.Textures.MaterialParamsTexture.Dispose();

        var updated = Texture2D.CreateWithData(
            width,
            height,
            PixelInternalFormat.Rgb16f,
            rgbTriplets,
            TextureFilterMode.Nearest,
            debugName: $"vge_materialParams_atlas_{atlasTextureId}");

        pagesByAtlasTexId[atlasTextureId] = new PageEntry(width, height, new MaterialAtlasPageTextures(updated, existingNormalDepth));
    }

    private static MaterialAtlasPageTextures CreatePageTextures(int atlasTextureId, int width, int height, bool enableNormalDepth)
    {
        // Initialize with defaults; contents get populated on BlockTexturesLoaded.
        float[] defaultParams = new float[checked(width * height * 3)];
        MaterialAtlasParamsBuilder.FillRgbTriplets(
            defaultParams,
            MaterialAtlasParamsBuilder.DefaultRoughness,
            MaterialAtlasParamsBuilder.DefaultMetallic,
            MaterialAtlasParamsBuilder.DefaultEmissive);

        var materialParamsTex = Texture2D.CreateWithData(
            width,
            height,
            PixelInternalFormat.Rgb16f,
            defaultParams,
            TextureFilterMode.Nearest,
            debugName: $"vge_materialParams_atlas_{atlasTextureId}");

        Texture2D? normalDepthTex = null;
        if (enableNormalDepth)
        {
            // Initialize to an identity normal with neutral height so terrain shading stays stable
            // while the async normal+depth pipeline populates tiles.
            float[] defaultNormalDepth = new float[checked(width * height * 4)];
            // Height is encoded into [0,1] where 0.5 is neutral (signed height 0).
            SimdSpanMath.FillInterleaved4(defaultNormalDepth, 0.5f, 0.5f, 1.0f, 0.5f);

            normalDepthTex = Texture2D.CreateWithData(
                width,
                height,
                PixelInternalFormat.Rgba16f,
                defaultNormalDepth,
                TextureFilterMode.Nearest,
                debugName: $"vge_normalDepth_atlas_{atlasTextureId}");
        }

        return new MaterialAtlasPageTextures(materialParamsTex, normalDepthTex);
    }

    public void Dispose()
    {
        foreach (PageEntry entry in pagesByAtlasTexId.Values)
        {
            entry.Textures.Dispose();
        }

        pagesByAtlasTexId.Clear();
    }
}
