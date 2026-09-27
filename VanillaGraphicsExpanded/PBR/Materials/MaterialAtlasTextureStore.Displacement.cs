using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Publishes opt-in amplitude metadata separately from BRDF and baked height channels.</summary>
internal sealed partial class MaterialAtlasTextureStore
{
    private readonly Dictionary<int, Texture2D> displacementByAtlas = new();

    #region Displacement metadata
    /// <summary>Rebuilds metadata from the current resolved plan, never from a stale height/BRDF disk cache.</summary>
    internal void UpdateDisplacement(AtlasBuildPlan plan)
    {
        // Clear old generations first; missing/newly unassigned tiles always resolve to zero.
        foreach (var texture in displacementByAtlas.Values) texture.Dispose();
        displacementByAtlas.Clear();
        foreach (var page in plan.Pages)
        {
            var tiles = plan.MaterialParamsTiles.Where(tile => tile.AtlasTextureId == page.AtlasTextureId
                && tile.Definition.DisplacementAmplitudeMetres > 0).ToArray();
            if (tiles.Length == 0) continue;
            var values = new float[checked(page.Width * page.Height)];
            foreach (var tile in tiles)
            {
                var rect = tile.Rect;
                if (rect.X < 0 || rect.Y < 0 || rect.Right > page.Width || rect.Bottom > page.Height) continue;
                float amplitude = MaterialDisplacement.ResolveAmplitude(tile.Definition.DisplacementAmplitudeMetres);
                for (int y = rect.Y; y < rect.Bottom; y++)
                    values.AsSpan(y * page.Width + rect.X, rect.Width).Fill(amplitude);
            }
            displacementByAtlas.Add(page.AtlasTextureId, Texture2D.CreateWithDataImmediate(page.Width, page.Height,
                PixelInternalFormat.R32f, values, TextureFilterMode.Nearest, $"vge_displacement_{page.AtlasTextureId}"));
        }
    }

    /// <summary>Returns current metadata only for an existing page with a valid neutral-initialized height atlas.</summary>
    internal bool TryGetDisplacementTexture(int atlasTextureId, out Texture2D? texture)
    {
        texture = null;
        return TryGetNormalDepthTextureId(atlasTextureId, out _)
            && displacementByAtlas.TryGetValue(atlasTextureId, out texture) && texture.IsValid;
    }
    #endregion
}
