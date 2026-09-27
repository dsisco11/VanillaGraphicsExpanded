using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Publishes compact opt-in tile records separately from BRDF and baked height channels.</summary>
internal sealed partial class MaterialAtlasTextureStore
{
    private readonly Dictionary<int, MaterialDisplacementPage> displacementByAtlas = new();

    #region Displacement metadata
    /// <summary>Rebuilds metadata from the current resolved plan, never from a stale height/BRDF disk cache.</summary>
    internal void UpdateDisplacement(AtlasBuildPlan plan)
    {
        // Clear old generations first; missing/newly unassigned tiles always resolve to zero.
        foreach (var texture in displacementByAtlas.Values) texture.Dispose();
        displacementByAtlas.Clear();
        GpuSupport.Initialize();
        foreach (var page in plan.Pages)
        {
            var tiles = plan.MaterialParamsTiles.Where(tile => tile.AtlasTextureId == page.AtlasTextureId
                && tile.Definition.DisplacementAmplitudeMetres > 0).ToArray();
            if (tiles.Length == 0) continue;
            // One exact float index per atlas pixel; two RGBA texels per authored tile avoid
            // replicating a full rectangle at every pixel. Zero indices mean no material.
            if (tiles.Length > 1 << 24) continue;
            int recordWidth = Math.Min(256, checked(tiles.Length << 1));
            int recordHeight = checked(((tiles.Length << 1) + recordWidth - 1) / recordWidth);
            if (recordWidth > GpuSupport.MaxTextureSize || recordHeight > GpuSupport.MaxTextureSize) continue;
            var values = new float[checked(page.Width * page.Height)];
            var records = new float[checked((recordWidth * recordHeight) << 2)];
            for (int index = 0; index < tiles.Length; index++)
            {
                var tile = tiles[index];
                var rect = tile.Rect;
                if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0
                    || rect.Right > page.Width || rect.Bottom > page.Height) continue;
                float amplitude = MaterialDisplacement.ResolveAmplitude(tile.Definition.DisplacementAmplitudeMetres);
                if (amplitude == 0) continue;
                int offset = index << 3;
                records[offset] = (float)rect.X / page.Width;
                records[offset + 1] = (float)rect.Y / page.Height;
                records[offset + 2] = (float)rect.Width / page.Width;
                records[offset + 3] = (float)rect.Height / page.Height;
                records[offset + 4] = amplitude;
                for (int y = rect.Y; y < rect.Bottom; y++)
                    values.AsSpan(y * page.Width + rect.X, rect.Width).Fill(index + 1);
            }
            var indices = Texture2D.CreateWithDataImmediate(page.Width, page.Height,
                PixelInternalFormat.R32f, values, TextureFilterMode.Nearest, $"vge_displacement_{page.AtlasTextureId}");
            try
            {
                var table = Texture2D.CreateWithDataImmediate(recordWidth, recordHeight,
                    PixelInternalFormat.Rgba32f, records, TextureFilterMode.Nearest, $"vge_displacement_records_{page.AtlasTextureId}");
                displacementByAtlas.Add(page.AtlasTextureId, new(indices, table));
            }
            catch { indices.Dispose(); throw; }
        }
    }

    /// <summary>Returns current metadata only for an existing page with a valid neutral-initialized height atlas.</summary>
    internal bool TryGetDisplacementTextures(int atlasTextureId, out MaterialDisplacementPage? textures)
    {
        textures = null;
        return TryGetNormalDepthTextureId(atlasTextureId, out _)
            && displacementByAtlas.TryGetValue(atlasTextureId, out textures)
            && textures.Indices.IsValid && textures.Records.IsValid
            && pagesByAtlasTexId.TryGetValue(atlasTextureId, out var page)
            && textures.Indices.Width == page.Width && textures.Indices.Height == page.Height;
    }
    #endregion
}
