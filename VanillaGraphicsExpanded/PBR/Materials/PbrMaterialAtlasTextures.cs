using System;
using System.Collections.Generic;

using OpenTK.Graphics.OpenGL;

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

    private readonly Dictionary<int, DynamicTexture> materialParamsTexByAtlasTexId = new();
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

        // One material data texture per atlas texture id.
        // We build full float buffers and upload once per atlas page to keep GL calls low.
        var pixelBuffersByAtlasTexId = new Dictionary<int, float[]>(capacity: atlas.AtlasTextures.Count);
        var sizesByAtlasTexId = new Dictionary<int, (int width, int height)>(capacity: atlas.AtlasTextures.Count);

        // Defaults for unmapped textures.
        const float defaultRoughness = 0.85f;
        const float defaultMetallic = 0.0f;
        const float defaultEmissive = 0.0f;

        foreach (LoadedTexture atlasPage in atlas.AtlasTextures)
        {
            int texId = atlasPage.TextureId;
            if (texId == 0)
            {
                continue;
            }

            int width = atlasPage.Width;
            int height = atlasPage.Height;
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            sizesByAtlasTexId[texId] = (width, height);

            float[] pixels = new float[width * height * 3];
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i + 0] = defaultRoughness;
                pixels[i + 1] = defaultMetallic;
                pixels[i + 2] = defaultEmissive;
            }

            pixelBuffersByAtlasTexId[texId] = pixels;
        }

        int filledRects = 0;
        foreach ((AssetLocation texture, string _) in PbrMaterialRegistry.Instance.MaterialIdByTexture)
        {
            if (!PbrMaterialRegistry.Instance.TryGetMaterial(texture, out PbrMaterialDefinition definition))
            {
                continue;
            }

            TextureAtlasPosition texPos;
            try
            {
                texPos = atlas[texture];
            }
            catch
            {
                continue;
            }

            if (texPos is null)
            {
                continue;
            }

            if (!pixelBuffersByAtlasTexId.TryGetValue(texPos.atlasTextureId, out float[]? pixels))
            {
                // Not in block atlas (or atlas page not tracked)
                continue;
            }

            (int width, int height) = sizesByAtlasTexId[texPos.atlasTextureId];

            // TextureAtlasPosition coordinates are normalized (0..1) in atlas UV space.
            // Convert to integer pixel bounds.
            int x1 = Clamp((int)Math.Floor(texPos.x1 * width), 0, width - 1);
            int y1 = Clamp((int)Math.Floor(texPos.y1 * height), 0, height - 1);
            int x2 = Clamp((int)Math.Ceiling(texPos.x2 * width), 0, width);
            int y2 = Clamp((int)Math.Ceiling(texPos.y2 * height), 0, height);

            int rectWidth = x2 - x1;
            int rectHeight = y2 - y1;
            if (rectWidth <= 0 || rectHeight <= 0)
            {
                continue;
            }

            float roughness = Clamp01(definition.Roughness);
            float metallic = Clamp01(definition.Metallic);
            float emissive = Clamp01(definition.Emissive);

            for (int y = y1; y < y2; y++)
            {
                int rowBase = (y * width + x1) * 3;
                for (int x = 0; x < rectWidth; x++)
                {
                    int idx = rowBase + x * 3;
                    pixels[idx + 0] = roughness;
                    pixels[idx + 1] = metallic;
                    pixels[idx + 2] = emissive;
                }
            }

            filledRects++;
        }

        foreach ((int atlasTexId, float[] pixels) in pixelBuffersByAtlasTexId)
        {
            (int width, int height) = sizesByAtlasTexId[atlasTexId];

            var tex = DynamicTexture.CreateWithData(
                width,
                height,
                PixelInternalFormat.Rgb16f,
                pixels,
                TextureFilterMode.Nearest,
                debugName: $"vge_materialParams_atlas_{atlasTexId}");

            materialParamsTexByAtlasTexId[atlasTexId] = tex;
        }

        capi.Logger.Notification(
            "[VGE] Built material param atlas textures: {0} atlas page(s), {1} texture rect(s) filled",
            materialParamsTexByAtlasTexId.Count,
            filledRects);

        IsInitialized = materialParamsTexByAtlasTexId.Count > 0;
    }

    public bool TryGetMaterialParamsTextureId(int atlasTextureId, out int materialParamsTextureId)
    {
        if (materialParamsTexByAtlasTexId.TryGetValue(atlasTextureId, out DynamicTexture? tex) && tex.IsValid)
        {
            materialParamsTextureId = tex.TextureId;
            return true;
        }

        materialParamsTextureId = 0;
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
        foreach (DynamicTexture tex in materialParamsTexByAtlasTexId.Values)
        {
            tex.Dispose();
        }

        materialParamsTexByAtlasTexId.Clear();
        IsInitialized = false;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }
}
