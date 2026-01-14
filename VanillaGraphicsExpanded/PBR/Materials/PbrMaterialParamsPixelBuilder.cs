using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal static class PbrMaterialParamsPixelBuilder
{
    public readonly record struct Result(
        Dictionary<int, float[]> PixelBuffersByAtlasTexId,
        Dictionary<int, (int width, int height)> SizesByAtlasTexId,
        int FilledRects);

    // Defaults for unmapped textures.
    internal const float DefaultRoughness = 0.85f;
    internal const float DefaultMetallic = 0.0f;
    internal const float DefaultEmissive = 0.0f;

    public static Result BuildRgb16fPixelBuffers(
        IReadOnlyList<(int atlasTextureId, int width, int height)> atlasPages,
        IReadOnlyDictionary<AssetLocation, TextureAtlasPosition> texturePositions,
        IReadOnlyDictionary<AssetLocation, PbrMaterialDefinition> materialsByTexture)
    {
        if (atlasPages is null) throw new ArgumentNullException(nameof(atlasPages));
        if (texturePositions is null) throw new ArgumentNullException(nameof(texturePositions));
        if (materialsByTexture is null) throw new ArgumentNullException(nameof(materialsByTexture));

        var pixelBuffersByAtlasTexId = new Dictionary<int, float[]>(capacity: atlasPages.Count);
        var sizesByAtlasTexId = new Dictionary<int, (int width, int height)>(capacity: atlasPages.Count);

        foreach ((int atlasTextureId, int width, int height) in atlasPages)
        {
            if (atlasTextureId == 0 || width <= 0 || height <= 0)
            {
                continue;
            }

            sizesByAtlasTexId[atlasTextureId] = (width, height);

            float[] pixels = new float[width * height * 3];
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i + 0] = DefaultRoughness;
                pixels[i + 1] = DefaultMetallic;
                pixels[i + 2] = DefaultEmissive;
            }

            pixelBuffersByAtlasTexId[atlasTextureId] = pixels;
        }

        int filledRects = 0;
        foreach ((AssetLocation texture, PbrMaterialDefinition definition) in materialsByTexture)
        {
            if (!texturePositions.TryGetValue(texture, out TextureAtlasPosition? texPos) || texPos is null)
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

        return new Result(pixelBuffersByAtlasTexId, sizesByAtlasTexId, filledRects);
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
