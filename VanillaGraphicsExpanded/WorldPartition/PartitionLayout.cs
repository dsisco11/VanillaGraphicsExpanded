using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Double precision world-space point or extent.</summary>
internal readonly record struct PartitionPoint(double X, double Y, double Z);

/// <summary>Half-open world-space bounds; empty bounds select no cells.</summary>
internal readonly record struct PartitionBounds(PartitionPoint Min, PartitionPoint Max)
{
    /// <summary>Validates finite, ordered bounds.</summary>
    public void Validate()
    {
        if (!double.IsFinite(Min.X) || !double.IsFinite(Min.Y) || !double.IsFinite(Min.Z) ||
            !double.IsFinite(Max.X) || !double.IsFinite(Max.Y) || !double.IsFinite(Max.Z) ||
            Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z)
            throw new ArgumentException("Bounds must be finite and ordered.");
    }

    /// <summary>Expands each face by a nonnegative world-space margin.</summary>
    public PartitionBounds Expand(double margin) => new(
        new(Min.X - margin, Min.Y - margin, Min.Z - margin),
        new(Max.X + margin, Max.Y + margin, Max.Z + margin));
}

/// <summary>Signed coordinates on a fixed world-zero lattice.</summary>
internal readonly record struct PartitionCoordinate(long X, long Y, long Z);

/// <summary>Identity independent of content category and physical storage.</summary>
internal readonly record struct PartitionCellKey(long Instance, string World, PartitionCoordinate Coordinate);

/// <summary>Regular, axis-aligned layout with no configurable grid origin.</summary>
internal sealed record PartitionLayout
{
    public PartitionPoint Extent { get; }

    /// <summary>Creates a positive, finite layout.</summary>
    public PartitionLayout(in PartitionPoint extent)
    {
        if (!double.IsFinite(extent.X) || !double.IsFinite(extent.Y) || !double.IsFinite(extent.Z) ||
            extent.X <= 0 || extent.Y <= 0 || extent.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(extent));
        Extent = extent;
    }

    #region Coordinate conversion
    /// <summary>Maps positions using floor, including the negative half of the world.</summary>
    public PartitionCoordinate Coordinate(in PartitionPoint point) => new(
        checked((long)Math.Floor(point.X / Extent.X)), checked((long)Math.Floor(point.Y / Extent.Y)),
        checked((long)Math.Floor(point.Z / Extent.Z)));

    /// <summary>Returns the immutable half-open bounds of a logical cell.</summary>
    public PartitionBounds Bounds(in PartitionCoordinate coordinate) => new(
        new(coordinate.X * Extent.X, coordinate.Y * Extent.Y, coordinate.Z * Extent.Z),
        new((coordinate.X + 1d) * Extent.X, (coordinate.Y + 1d) * Extent.Y, (coordinate.Z + 1d) * Extent.Z));

    /// <summary>Converts half-open bounds to a discrete range, preserving exact negative boundaries.</summary>
    public PartitionCellRange Range(in PartitionBounds bounds)
    {
        bounds.Validate();
        if (bounds.Min.X == bounds.Max.X || bounds.Min.Y == bounds.Max.Y || bounds.Min.Z == bounds.Max.Z) return default;
        return new(Coordinate(bounds.Min), new(checked((long)Math.Ceiling(bounds.Max.X / Extent.X)),
            checked((long)Math.Ceiling(bounds.Max.Y / Extent.Y)), checked((long)Math.Ceiling(bounds.Max.Z / Extent.Z))));
    }

    /// <summary>Enumerates all intersecting cells rather than selecting by cell centers.</summary>
    public IEnumerable<PartitionCoordinate> Intersecting(in PartitionBounds bounds) => Range(bounds).Coordinates();
    #endregion
}
