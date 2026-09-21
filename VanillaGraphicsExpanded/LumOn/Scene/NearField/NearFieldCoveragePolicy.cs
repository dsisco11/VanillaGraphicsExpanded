using System;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Bounded origin domain and conservative trace reach for independent near-field geometry coverage.</summary>
internal sealed record NearFieldCoveragePolicy(double OriginRadius = 16, double PrefetchMargin = 16, int MaximumResolution = 192)
{
    public const int CellSize = 16;
    public const double AdjacentLightMargin = 1;
    // ChunkKey uses signed 21-bit ZigZag coordinates: [-2^20, 2^20) chunks.
    private const long SourceBlockLimit = (1L << 20) * 32;

    /// <summary>Includes every cache level and the secondary local sky segment used by hit lighting.</summary>
    public static double MaximumTraceReach(double rayMaximum, double baseSpacing, int levels) =>
        Math.Max(rayMaximum, 2 * Math.Sqrt(3) * baseSpacing * Math.Pow(2, levels - 1));

    /// <summary>Plans enough aligned cells for the complete source envelope or reports an unsupported configuration.</summary>
    public bool TryPlan(in PartitionPoint position, double traceReach, out NearFieldCoveragePlan plan)
    {
        plan = default;
        if (!double.IsFinite(traceReach) || traceReach <= 0 || OriginRadius <= 0 || PrefetchMargin < 0) return false;
        // A hit can launch a secondary sky test of at most 16 blocks, plus the adjacent lighting cell.
        double margin = traceReach + Math.Min(traceReach, 16) + AdjacentLightMargin;
        var origins = new PartitionBounds(new(position.X - OriginRadius, position.Y - OriginRadius, position.Z - OriginRadius),
            new(position.X + OriginRadius, position.Y + OriginRadius, position.Z + OriginRadius));
        PartitionBounds required = origins.Expand(margin);
        PartitionBounds prefetched = required.Expand(PrefetchMargin);
        double cells = Math.Ceiling(2 * (OriginRadius + margin + PrefetchMargin) / CellSize) + 1;
        if (cells * CellSize > MaximumResolution) return false;
        var layout = new PartitionLayout(new(CellSize, CellSize, CellSize));
        PartitionCellRange range = layout.Range(prefetched);
        // Reject both packed source-key aliasing and GPU coordinate overflow before allocating a window.
        if (range.Min.X * CellSize < -SourceBlockLimit || range.Min.Y * CellSize < -SourceBlockLimit || range.Min.Z * CellSize < -SourceBlockLimit ||
            (range.Min.X + cells) * CellSize > SourceBlockLimit || (range.Min.Y + cells) * CellSize > SourceBlockLimit || (range.Min.Z + cells) * CellSize > SourceBlockLimit) return false;
        if (range.Min.X * CellSize < int.MinValue || range.Min.Y * CellSize < int.MinValue || range.Min.Z * CellSize < int.MinValue ||
            (range.Min.X + cells) * CellSize > int.MaxValue || (range.Min.Y + cells) * CellSize > int.MaxValue || (range.Min.Z + cells) * CellSize > int.MaxValue) return false;
        plan = new(required, origins, new((int)(range.Min.X * CellSize), (int)(range.Min.Y * CellSize), (int)(range.Min.Z * CellSize)), (int)cells * CellSize, traceReach);
        return true;
    }
}

/// <summary>One coherent source-domain and GPU window plan.</summary>
internal readonly record struct NearFieldCoveragePlan(PartitionBounds Required, PartitionBounds Origins, VectorInt3 WindowOrigin, int Resolution, double TraceReach);
