using VanillaGraphicsExpanded.Rendering.RHI;
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
internal sealed partial class MaterialAtlasTextureStore : IDisposable
{
    /// <summary>Retains textures and their admitted emission bound for the same atlas lifetime.</summary>
    private sealed record class PageEntry(int Width, int Height, MaterialAtlasPageTextures Textures);

    private readonly Dictionary<int, PageEntry> pagesByAtlasTexId = new();

    #region Public API
    #region Publication
    /// <summary>Reports the number of owned atlas publications.</summary>
    public int PageCount => pagesByAtlasTexId.Count;

    /// <summary>Reports whether any material atlas is available.</summary>
    public bool HasAnyTextures => pagesByAtlasTexId.Count > 0;

    /// <summary>Bounds actual uploaded material emission, including binary16 rounding and retained tile history.</summary>
    internal float MaximumEmissionNits => pagesByAtlasTexId.Values.Select(page => page.Textures.MaximumEmissionNits).DefaultIfEmpty(0).Max();

    /// <summary>Admits material data before upload and publishes its source bound only after the write succeeds.</summary>
    internal bool TryUploadMaterialParamsTile(int atlasTextureId, AtlasRect rect, float[] rgbTriplets, float transmission, bool immediate = false)
    {
        ArgumentNullException.ThrowIfNull(rgbTriplets);
        if (!pagesByAtlasTexId.TryGetValue(atlasTextureId, out var entry)) return false;
        float[] packed = MaterialTransmission.Pack(rgbTriplets, transmission);
        return entry.Textures.TryPublishMaterial(entry.Textures.MaterialParamsTexture.TextureId, rect, packed, immediate);
    }

    /// <summary>Resolves only texture sets currently owned by this store.</summary>
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
    #endregion

    #region Lifetime
    /// <summary>Detects page membership, dimensions or normal-map ownership changes.</summary>
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

    /// <summary>Reconciles page lifetimes without carrying source bounds across replacement allocations.</summary>
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
                if (displacementByAtlas.Remove(existingId, out var removedDisplacement)) removedDisplacement.Dispose();
                if (waterMediumByAtlas.Remove(existingId, out var removedWater)) removedWater.Dispose();
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
                if (displacementByAtlas.Remove(atlasTexId, out var resizedDisplacement)) resizedDisplacement.Dispose();
                if (waterMediumByAtlas.Remove(atlasTexId, out var resizedWater)) resizedWater.Dispose();
            }

            pagesByAtlasTexId[atlasTexId] = new PageEntry(width, height, CreatePageTextures(atlasTexId, width, height, enableNormalDepth));
        }
    }

    /// <summary>Retires textures and their source-bound metadata together.</summary>
    public void Dispose()
    {
        foreach (PageEntry entry in pagesByAtlasTexId.Values)
        {
            entry.Textures.Dispose();
        }

        pagesByAtlasTexId.Clear();
        foreach (var texture in displacementByAtlas.Values) texture.Dispose();
        displacementByAtlas.Clear();
        foreach (var texture in waterMediumByAtlas.Values) texture.Dispose();
        waterMediumByAtlas.Clear();
    }
    #endregion

    #region Material textures
    /// <summary>Resolves an admitted material texture only while its owned image remains valid.</summary>
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

    /// <summary>Resolves the optional current normal/depth atlas image.</summary>
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

    /// <summary>Atomically replaces material data after admission, preserving compatible source history bounds.</summary>
    public void ReplaceMaterialParamsTextureWithData(int atlasTextureId, int width, int height, float[] rgbTriplets)
    {
        ArgumentNullException.ThrowIfNull(rgbTriplets);

        if (!pagesByAtlasTexId.TryGetValue(atlasTextureId, out PageEntry? entry))
        {
            return;
        }

        Texture2D? existingNormalDepth = entry.Textures.NormalDepthTexture;
        float[] packed = MaterialTransmission.Pack(rgbTriplets);
        float emission = AdmittedEmissionMaximum(packed);

        var updated = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = width, Height = height, Format = TextureFormat.Rgba16f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = $"vge_materialParams_atlas_{atlasTextureId}" }, [new RHITextureInitialData(new(new(), 0, 0, 0, width, height), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba16f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((packed).AsSpan()).ToArray())]).RequireResource();

        // Keep the previous publication intact until validation and replacement allocation succeed.
        float retainedEmission = entry.Textures.RetireMaterial();
        pagesByAtlasTexId[atlasTextureId] = new PageEntry(width, height,
            new MaterialAtlasPageTextures(updated, existingNormalDepth, Math.Max(retainedEmission, emission)));
    }
    #endregion

    #region Admission
    /// <summary>Rejects unsupported material storage and bounds the neutral emission actually decoded from the atlas.</summary>
    internal static float AdmittedEmissionMaximum(float[] packed)
    {
        float coefficient = 0;
        for (int index = 0; index < packed.Length; index++)
        {
            float value = packed[index];
            if (!float.IsFinite(value) || Math.Abs(value) > 65504)
                throw new ArgumentOutOfRangeException(nameof(packed), "Material parameters must fit finite binary16 storage.");
            if ((index & 3) == 2 && value > 0)
            {
                Half endpoint = (Half)value;
                // Publish the outward endpoint, not an assumed GPU rounding direction.
                // This also covers a positive coefficient below the first half subnormal.
                if ((float)endpoint < value)
                    endpoint = BitConverter.UInt16BitsToHalf((ushort)(BitConverter.HalfToUInt16Bits(endpoint) + 1));
                coefficient = Math.Max(coefficient, (float)endpoint);
            }
        }
        return LightingStorage.UpperBound((double)coefficient * LightingCalibration.EmissionReferenceNits);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Allocates a complete default texture set with zero material emission.</summary>
    private static MaterialAtlasPageTextures CreatePageTextures(int atlasTextureId, int width, int height, bool enableNormalDepth)
    {
        // Initialize with defaults; contents get populated on BlockTexturesLoaded.
        float[] defaultParams = new float[checked(width * height * 3)];
        MaterialAtlasParamsBuilder.FillRgbTriplets(
            defaultParams,
            MaterialAtlasParamsBuilder.DefaultRoughness,
            MaterialAtlasParamsBuilder.DefaultMetallic,
            MaterialAtlasParamsBuilder.DefaultEmissive);

        var materialParamsTex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = width, Height = height, Format = TextureFormat.Rgba16f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = $"vge_materialParams_atlas_{atlasTextureId}" }, [new RHITextureInitialData(new(new(), 0, 0, 0, width, height), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba16f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((MaterialTransmission.Pack(defaultParams)).AsSpan()).ToArray())]).RequireResource();

        Texture2D? normalDepthTex = null;
        if (enableNormalDepth)
        {
            // Initialize to an identity normal with neutral height so terrain shading stays stable
            // while the async normal+depth pipeline populates tiles.
            float[] defaultNormalDepth = new float[checked(width * height * 4)];
            // Height is encoded into [0,1] where 0.5 is neutral (signed height 0).
            SimdSpanMath.FillInterleaved4(defaultNormalDepth, 0.5f, 0.5f, 1.0f, 0.5f);

            normalDepthTex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = width, Height = height, Format = TextureFormat.Rgba16f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = $"vge_normalDepth_atlas_{atlasTextureId}" }, [new RHITextureInitialData(new(new(), 0, 0, 0, width, height), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba16f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((defaultNormalDepth).AsSpan()).ToArray())]).RequireResource();
        }

        return new MaterialAtlasPageTextures(materialParamsTex, normalDepthTex);
    }

    #endregion
}
