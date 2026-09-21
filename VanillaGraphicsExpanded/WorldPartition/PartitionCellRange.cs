using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Half-open integer range supporting incremental rectangular coverage differences.</summary>
internal readonly record struct PartitionCellRange(PartitionCoordinate Min, PartitionCoordinate End)
{
    public bool Empty => Min.X >= End.X || Min.Y >= End.Y || Min.Z >= End.Z;

    /// <summary>Enumerates this bounded range without changing the fixed grid.</summary>
    public IEnumerable<PartitionCoordinate> Coordinates()
    {
        for (long z = Min.Z; z < End.Z; ++z)
        for (long y = Min.Y; y < End.Y; ++y)
        for (long x = Min.X; x < End.X; ++x) yield return new(x, y, z);
    }

    /// <summary>Enumerates only entering/leaving slabs, never the overlapping interior.</summary>
    // Iterator state must own its arguments; C# iterator methods cannot accept in/ref/out parameters.
    public IEnumerable<PartitionCoordinate> Except(PartitionCellRange other)
    {
        if (Empty) yield break;
        var overlap = new PartitionCellRange(new(Math.Max(Min.X, other.Min.X), Math.Max(Min.Y, other.Min.Y), Math.Max(Min.Z, other.Min.Z)),
            new(Math.Min(End.X, other.End.X), Math.Min(End.Y, other.End.Y), Math.Min(End.Z, other.End.Z)));
        if (overlap.Empty)
        {
            foreach (PartitionCoordinate coordinate in Coordinates()) yield return coordinate;
            yield break;
        }
        // Disjoint slabs successively restrict the other axes to avoid duplicate edge/corner cells.
        PartitionCellRange[] slabs = {
            new(Min, new(overlap.Min.X, End.Y, End.Z)),
            new(new(overlap.End.X, Min.Y, Min.Z), End),
            new(new(overlap.Min.X, Min.Y, Min.Z), new(overlap.End.X, overlap.Min.Y, End.Z)),
            new(new(overlap.Min.X, overlap.End.Y, Min.Z), new(overlap.End.X, End.Y, End.Z)),
            new(new(overlap.Min.X, overlap.Min.Y, Min.Z), new(overlap.End.X, overlap.End.Y, overlap.Min.Z)),
            new(new(overlap.Min.X, overlap.Min.Y, overlap.End.Z), new(overlap.End.X, overlap.End.Y, End.Z)) };
        foreach (PartitionCellRange slab in slabs)
            foreach (PartitionCoordinate coordinate in slab.Coordinates()) yield return coordinate;
    }
}

/// <summary>Cached discrete source ranges; subcell movement does not enumerate coverage again.</summary>
internal readonly record struct PartitionSourceCoverage(PartitionCellRange Required, PartitionCellRange Prefetch);
