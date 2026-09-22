using System;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Independent logical consumer domains sharing a cell-aligned physical ring.</summary>
internal sealed record TraceGeometryCoverage(PartitionBounds? NearField, PartitionBounds? Surface,
    PartitionBounds Window, int Resolution, int WorldHeight)
{
    public const int CellSize = 16;
    private const long WorldLimit = (1L << 20) * 32;

    #region Coverage planning
    /// <summary>Maps signed 16-block publication cells to their containing 32-block source chunk.</summary>
    public static ChunkKey SourceChunk(in PartitionCoordinate coordinate)
    {
        long x = coordinate.X >> 1, y = coordinate.Y >> 1, z = coordinate.Z >> 1;
        const long limit = 1L << 20;
        if (x < -limit || x >= limit || y < -limit || y >= limit || z < -limit || z >= limit)
            throw new ArgumentOutOfRangeException(nameof(coordinate), "Source chunk exceeds its packed coordinate range.");
        return ChunkKey.FromChunkCoords((int)x, (int)y, (int)z);
    }
    /// <summary>Preserves the old L0 camera-floor box and the fixed near-field box without expanding either domain.</summary>
    public static TraceGeometryCoverage Plan(in PartitionPoint camera, bool nearField, int? surfaceResolution, int worldHeight)
    {
        if (!nearField && surfaceResolution == null) throw new ArgumentException("No geometry consumer requested.");
        if (worldHeight <= 0 || surfaceResolution is not (null or 16 or 32 or 64 or 128)) throw new ArgumentOutOfRangeException(nameof(surfaceResolution));
        if (!double.IsFinite(camera.X) || !double.IsFinite(camera.Y) || !double.IsFinite(camera.Z)) throw new ArgumentException("Nonfinite camera.");
        var nearMin = new PartitionPoint((Math.Floor(camera.X / 16) - 1) * 16,
            (Math.Floor(camera.Y / 16) - 1) * 16, (Math.Floor(camera.Z / 16) - 1) * 16);
        PartitionBounds? near = nearField ? new(nearMin, new(nearMin.X + 48, nearMin.Y + 48, nearMin.Z + 48)) : null;
        PartitionBounds? surface = surfaceResolution is int n ? new(new(Math.Floor(camera.X) - n / 2,
            Math.Floor(camera.Y) - n / 2, Math.Floor(camera.Z) - n / 2), new(Math.Floor(camera.X) + n / 2,
            Math.Floor(camera.Y) + n / 2, Math.Floor(camera.Z) + n / 2)) : null;
        PartitionPoint a = (near ?? surface!.Value).Min, b = (surface ?? near!.Value).Min;
        var min = new PartitionPoint(Math.Floor(Math.Min(a.X, b.X) / 16) * 16,
            Math.Floor(Math.Min(a.Y, b.Y) / 16) * 16, Math.Floor(Math.Min(a.Z, b.Z) / 16) * 16);
        int r = surfaceResolution.HasValue ? Math.Max(48, surfaceResolution.Value + 16) : 48;
        var max = new PartitionPoint(min.X + r, min.Y + r, min.Z + r);
        if (min.X < -WorldLimit || min.Y < -WorldLimit || min.Z < -WorldLimit ||
            max.X > WorldLimit || max.Y > WorldLimit || max.Z > WorldLimit) throw new ArgumentOutOfRangeException(nameof(camera));
        return new(near, surface, new(min, max), r, worldHeight);
    }

    /// <summary>Clips demand while retaining original logical bounds for shader traversal.</summary>
    public PartitionBounds Clip(in PartitionBounds bounds) => new(
        new(bounds.Min.X, Math.Clamp(bounds.Min.Y, 0, WorldHeight), bounds.Min.Z),
        new(bounds.Max.X, Math.Clamp(bounds.Max.Y, 0, WorldHeight), bounds.Max.Z));

    /// <summary>Returns whether a cell belongs to the higher-priority consumer.</summary>
    public bool IsNear(in PartitionCoordinate cell) => NearField is { } bounds &&
        cell.X * 16 < bounds.Max.X && (cell.X + 1) * 16 > bounds.Min.X &&
        cell.Y * 16 < Math.Min(bounds.Max.Y, WorldHeight) && (cell.Y + 1) * 16 > Math.Max(bounds.Min.Y, 0) &&
        cell.Z * 16 < bounds.Max.Z && (cell.Z + 1) * 16 > bounds.Min.Z;
    #endregion
}
