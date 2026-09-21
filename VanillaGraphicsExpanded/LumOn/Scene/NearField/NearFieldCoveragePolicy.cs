using System;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Selects the camera cell and its immediate neighbors on the fixed world grid.</summary>
internal sealed record NearFieldCoveragePolicy
{
    public const int CellSize = 16;
    public const int WindowCells = 3;
    public const int WindowResolution = CellSize * WindowCells;
    public double OriginRadius => WindowResolution / 2;
    public double PrefetchMargin => 0;
    private const long SourceBlockLimit = (1L << 20) * 32;

    #region Window planning
    /// <summary>Limits requested traversal, independently of the fixed geometry allocation.</summary>
    public static double MaximumTraceReach(double rayMaximum, double baseSpacing, int levels) =>
        Math.Max(rayMaximum, 2 * Math.Sqrt(3) * baseSpacing * Math.Pow(2, levels - 1));

    /// <summary>Builds exactly 27 slots; demand excludes cells outside the world's vertical domain.</summary>
    public bool TryPlan(in PartitionPoint position, double traceReach, out NearFieldCoveragePlan plan, int? worldHeight = null)
    {
        plan = default;
        if (!double.IsFinite(traceReach) || traceReach <= 0 || worldHeight <= 0) return false;
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z)) return false;
        // Floor before centering so negative coordinates use the same zero-origin lattice.
        double x = (Math.Floor(position.X / CellSize) - 1) * CellSize;
        double y = (Math.Floor(position.Y / CellSize) - 1) * CellSize;
        double z = (Math.Floor(position.Z / CellSize) - 1) * CellSize;
        if (x < -SourceBlockLimit || y < -SourceBlockLimit || z < -SourceBlockLimit ||
            x + WindowResolution > SourceBlockLimit || y + WindowResolution > SourceBlockLimit || z + WindowResolution > SourceBlockLimit) return false;
        var origins = new PartitionBounds(new(x, y, z), new(x + WindowResolution, y + WindowResolution, z + WindowResolution));
        double bottom = worldHeight.HasValue ? Math.Max(0, y) : y;
        double top = worldHeight.HasValue ? Math.Min(worldHeight.Value, y + WindowResolution) : y + WindowResolution;
        if (bottom >= top) return false;
        var required = new PartitionBounds(new(x, bottom, z), new(x + WindowResolution, top, z + WindowResolution));
        // Origins describe addressability, not a promise that every ray fits: exiting the
        // window remains unavailable in the tracer and never authorizes cache reuse.
        plan = new(required, origins, new((int)x, (int)y, (int)z), WindowResolution, traceReach);
        return true;
    }
    #endregion
}

/// <summary>Fixed physical window, valid-world demand and requested traversal limit.</summary>
internal readonly record struct NearFieldCoveragePlan(PartitionBounds Required, PartitionBounds Origins, VectorInt3 WindowOrigin, int Resolution, double TraceReach);
