using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Support.Lighting;

/// <summary>Plans exact requested-tile GPU observations without including unused atlas texels.</summary>
internal static class SurfaceCacheReadiness
{
    /// <summary>Describes one contiguous requested tile run in a single atlas layer and row.</summary>
    internal readonly record struct Region(int X, int Y, int Width, int Height, int Layer);

    #region Region planning
    /// <summary>Coalesces sorted, distinct one-based physical pages without crossing row, layer, or unrequested gaps.</summary>
    internal static ImmutableArray<Region> Plan(IEnumerable<uint> physicalPages, int tileSize, int tilesPerAxis, int tilesPerAtlas)
    {
        ArgumentNullException.ThrowIfNull(physicalPages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tilesPerAxis);
        if (tilesPerAtlas <= 0 || tilesPerAtlas > (long)tilesPerAxis * tilesPerAxis)
            throw new ArgumentOutOfRangeException(nameof(tilesPerAtlas));
        var regions = ImmutableArray.CreateBuilder<Region>();
        foreach (uint page in physicalPages.Distinct().Order())
        {
            if (page == 0) throw new ArgumentOutOfRangeException(nameof(physicalPages), "Physical page zero is reserved.");
            int physical = checked((int)(page - 1));
            int local = physical % tilesPerAtlas;
            var next = new Region(checked((local % tilesPerAxis) * tileSize),
                checked((local / tilesPerAxis) * tileSize), tileSize, tileSize, physical / tilesPerAtlas);
            // Sorting makes adjacent regions mergeable only when every intervening physical tile was requested.
            if (regions.Count > 0 && regions[^1] is var previous && previous.Layer == next.Layer &&
                previous.Y == next.Y && checked(previous.X + previous.Width) == next.X)
                regions[^1] = previous with { Width = checked(previous.Width + tileSize) };
            else regions.Add(next);
        }
        return regions.ToImmutable();
    }

    #endregion

    #region Initialized observation
    /// <summary>Checks every requested alpha on the GPU without transferring page contents to the CPU.</summary>
    internal static bool AllInitialized(Texture3D texture, ImmutableArray<Region> regions, Assertions.GpuImageComparisons comparisons)
    {
        if (regions.IsDefaultOrEmpty) return false;
        foreach (var region in regions)
        {
            var result = comparisons.CompareConstant(texture, 1,
                new(new(0, region.Layer), region.X, region.Y, 0, region.Width, region.Height), channelMask: 8);
            if (result.Mismatches != 0) return false;
        }
        return true;
    }
    #endregion
}
