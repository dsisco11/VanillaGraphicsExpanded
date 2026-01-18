using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Immutable snapshot of the block texture atlas state at a point in time.
/// Captures page sizes/IDs and the raw <see cref="TextureAtlasPosition"/> array so planning can be decoupled
/// from live atlas mutation.
/// </summary>
internal sealed record class AtlasSnapshot(
    IReadOnlyList<AtlasSnapshot.AtlasPage> Pages,
    TextureAtlasPosition?[] Positions,
    short ReloadIteration,
    int NonNullPositionCount)
{
    internal readonly record struct AtlasPage(int AtlasTextureId, int Width, int Height);

    public static AtlasSnapshot Capture(IBlockTextureAtlasAPI atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);

        var pages = new List<AtlasPage>(capacity: atlas.AtlasTextures.Count);
        foreach (LoadedTexture atlasPage in atlas.AtlasTextures)
        {
            if (atlasPage.TextureId == 0 || atlasPage.Width <= 0 || atlasPage.Height <= 0)
            {
                continue;
            }

            pages.Add(new AtlasPage(atlasPage.TextureId, atlasPage.Width, atlasPage.Height));
        }

        TextureAtlasPosition?[] positions = atlas.Positions?.ToArray() ?? Array.Empty<TextureAtlasPosition?>();

        short reloadIteration = -1;
        int nonNullCount = 0;
        foreach (TextureAtlasPosition? pos in positions)
        {
            if (pos is null)
            {
                continue;
            }

            nonNullCount++;
            if (reloadIteration < 0)
            {
                reloadIteration = pos.reloadIteration;
            }
        }

        return new AtlasSnapshot(pages, positions, reloadIteration, nonNullCount);
    }
}
