using System;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 23.1: Shared coordinate + ring-mapping helpers for the TraceScene occupancy clipmap.
/// </summary>
internal static class LumonSceneTraceSceneClipmapMath
{
    // World-cell region size (32^3).
    public const int RegionSize = 32;
    public const int RegionShift = 5; // log2(32)
    public const int RegionMask = RegionSize - 1;

    public static VectorInt3 WorldCellToRegionCoord(in VectorInt3 worldCell)
        // Arithmetic shift is floorDiv for power-of-two sizes (including negatives).
        => new(worldCell.X >> RegionShift, worldCell.Y >> RegionShift, worldCell.Z >> RegionShift);

    public static VectorInt3 WorldCellToLocalCell(in VectorInt3 worldCell)
    {
        VectorInt3 region = WorldCellToRegionCoord(worldCell);
        return new VectorInt3(
            worldCell.X - (region.X << RegionShift),
            worldCell.Y - (region.Y << RegionShift),
            worldCell.Z - (region.Z << RegionShift));
    }

    /// <summary>
    /// Maps a worldCell to the clipmap cell coordinate at <paramref name="level"/> (spacing = 2^level).
    /// </summary>
    public static VectorInt3 WorldCellToLevelCell(in VectorInt3 worldCell, int level)
    {
        if ((uint)level > 30u) throw new ArgumentOutOfRangeException(nameof(level));
        return new VectorInt3(worldCell.X >> level, worldCell.Y >> level, worldCell.Z >> level);
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="levelCell"/> lies within the clipmap window defined by <paramref name="originMinCell"/> and <paramref name="resolution"/>.
    /// </summary>
    public static bool IsInBounds(in VectorInt3 levelCell, in VectorInt3 originMinCell, int resolution)
    {
        if (resolution <= 0) return false;

        int lx = levelCell.X - originMinCell.X;
        int ly = levelCell.Y - originMinCell.Y;
        int lz = levelCell.Z - originMinCell.Z;

        return (uint)lx < (uint)resolution
               && (uint)ly < (uint)resolution
               && (uint)lz < (uint)resolution;
    }

    /// <summary>
    /// Maps a world-space clipmap cell coordinate into texture coordinates in the ring-buffered 3D texture.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// - Local = levelCell - originMinCell
    /// - Tex = Wrap(Local + ring, resolution)
    /// </remarks>
    public static bool TryMapLevelCellToTexel(
        in VectorInt3 levelCell,
        in VectorInt3 originMinCell,
        in VectorInt3 ring,
        int resolution,
        out VectorInt3 texel)
    {
        if (resolution <= 0)
        {
            texel = default;
            return false;
        }

        int lx = levelCell.X - originMinCell.X;
        int ly = levelCell.Y - originMinCell.Y;
        int lz = levelCell.Z - originMinCell.Z;

        if ((uint)lx >= (uint)resolution || (uint)ly >= (uint)resolution || (uint)lz >= (uint)resolution)
        {
            texel = default;
            return false;
        }

        texel = new VectorInt3(
            Wrap(lx + ring.X, resolution),
            Wrap(ly + ring.Y, resolution),
            Wrap(lz + ring.Z, resolution));

        return true;
    }

    public static int Wrap(int x, int m)
    {
        int r = x % m;
        return r < 0 ? r + m : r;
    }
}

