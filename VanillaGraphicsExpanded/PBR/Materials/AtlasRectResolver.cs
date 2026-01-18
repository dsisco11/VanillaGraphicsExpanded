using System;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Converts normalized atlas UV bounds (<see cref="TextureAtlasPosition"/>) to integer pixel rectangles.
/// Uses the project-wide rounding/clamping rules (floor for min, ceil for max).
/// </summary>
internal static class AtlasRectResolver
{
    public static bool TryResolvePixelRect(TextureAtlasPosition? position, int atlasWidth, int atlasHeight, out AtlasRect rect)
    {
        if (position is null || atlasWidth <= 0 || atlasHeight <= 0)
        {
            rect = default;
            return false;
        }

        int x1 = Math.Clamp((int)Math.Floor(position.x1 * atlasWidth), 0, atlasWidth - 1);
        int y1 = Math.Clamp((int)Math.Floor(position.y1 * atlasHeight), 0, atlasHeight - 1);
        int x2 = Math.Clamp((int)Math.Ceiling(position.x2 * atlasWidth), 0, atlasWidth);
        int y2 = Math.Clamp((int)Math.Ceiling(position.y2 * atlasHeight), 0, atlasHeight);

        int width = x2 - x1;
        int height = y2 - y1;

        if (width <= 0 || height <= 0)
        {
            rect = default;
            return false;
        }

        rect = new AtlasRect(x1, y1, width, height);
        return true;
    }
}
