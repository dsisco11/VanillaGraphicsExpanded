using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Publishes water metadata from current definitions, independently of cached BRDF texture channels.</summary>
internal sealed partial class MaterialAtlasTextureStore
{
    private readonly Dictionary<int, MaterialWaterMediumPage> waterMediumByAtlas = new();

    #region Public API
    /// <summary>Replaces the coefficient generation whenever the resolved atlas plan changes.</summary>
    internal void UpdateWaterMedium(AtlasBuildPlan plan)
    {
        // Zero indices mean measured clear water. Only transmitting tiles need authored records;
        // overrides change BRDF images, but the mapped material still owns its medium.
        foreach (var resource in waterMediumByAtlas.Values) resource.Dispose();
        waterMediumByAtlas.Clear();
        GpuSupport.Initialize();
        foreach (var page in plan.Pages)
        {
            var tiles = plan.MaterialParamsTiles.Where(tile => tile.AtlasTextureId == page.AtlasTextureId
                && tile.Definition.Transmission > 0).ToArray();
            var overrides = plan.MaterialParamsOverrides.Where(tile => tile.AtlasTextureId == page.AtlasTextureId)
                .Where(tile => PbrMaterialRegistry.Instance.TryGetMaterial(tile.TargetTexture, out var material)
                    && material.Transmission > 0).ToArray();
            int count = checked(tiles.Length + overrides.Length);
            if (count == 0) continue;
            int width = Math.Min(256, checked(count * 2));
            int height = checked((count * 2 + width - 1) / width);
            if (count > 1 << 24 || width > GpuSupport.MaxTextureSize || height > GpuSupport.MaxTextureSize)
                throw new InvalidOperationException("Water medium table exceeds supported texture dimensions.");
            var indices = new float[checked(page.Width * page.Height)];
            var records = new float[checked(width * height * 4)];
            int record = 0;
            foreach (var tile in tiles)
                WriteWaterRecord(page, tile.Rect, tile.Definition.WaterMedium ?? WaterMedium.Clear, record++, indices, records);
            foreach (var tile in overrides)
            {
                PbrMaterialRegistry.Instance.TryGetMaterial(tile.TargetTexture, out var material);
                WriteWaterRecord(page, tile.Rect, material.WaterMedium ?? WaterMedium.Clear, record++, indices, records);
            }
            var indexTexture = Texture2D.CreateWithDataImmediate(page.Width, page.Height,
                PixelInternalFormat.R32f, indices, TextureFilterMode.Nearest, $"vge_water_indices_{page.AtlasTextureId}");
            try
            {
                var recordTexture = Texture2D.CreateWithDataImmediate(width, height,
                    PixelInternalFormat.Rgba32f, records, TextureFilterMode.Nearest, $"vge_water_records_{page.AtlasTextureId}");
                waterMediumByAtlas.Add(page.AtlasTextureId, new(indexTexture, recordTexture));
            }
            catch { indexTexture.Dispose(); throw; }
        }
    }

    /// <summary>Returns a current dimension-matched table, or leaves the shader on its measured clear-water default.</summary>
    internal bool TryGetWaterMediumTextures(int atlasTextureId, out MaterialWaterMediumPage? textures)
    {
        textures = null;
        return waterMediumByAtlas.TryGetValue(atlasTextureId, out textures)
            && textures.Indices.IsValid && textures.Records.IsValid
            && pagesByAtlasTexId.TryGetValue(atlasTextureId, out var page)
            && textures.Indices.Width == page.Width && textures.Indices.Height == page.Height;
    }
    #endregion

    #region Private
    /// <summary>Writes one validated tile rectangle and two RGBA coefficient records with exact integer indices.</summary>
    private static void WriteWaterRecord(AtlasBuildPlan.AtlasPagePlan page, AtlasRect rect, WaterMedium medium,
        int index, float[] indices, float[] records)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0
            || rect.Right > page.Width || rect.Bottom > page.Height) return;
        var absorption = medium.AbsorptionPerMetre;
        var scattering = medium.EffectiveScatteringPerMetre;
        int offset = index * 8;
        records[offset] = absorption.X;
        records[offset + 1] = absorption.Y;
        records[offset + 2] = absorption.Z;
        records[offset + 3] = medium.Anisotropy;
        records[offset + 4] = scattering.X;
        records[offset + 5] = scattering.Y;
        records[offset + 6] = scattering.Z;
        for (int y = rect.Y; y < rect.Bottom; y++)
            indices.AsSpan(y * page.Width + rect.X, rect.Width).Fill(index + 1);
    }
    #endregion
}
