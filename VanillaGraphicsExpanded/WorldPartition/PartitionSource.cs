using System;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Immutable streaming source targeted to one registration; callers fan out shared sources explicitly.</summary>
internal sealed record PartitionSource(long Id, long Instance, string World, PartitionPoint Position,
    PartitionBounds Required, double Priority = 0, PartitionBounds? Loaded = null);

/// <summary>Spatial and time limits on speculative residency.</summary>
internal sealed record PartitionCoveragePolicy(double PrefetchMargin, double RetentionMargin, long RetentionTicks)
{
    /// <summary>Rejects negative or unbounded retention configuration.</summary>
    public void Validate()
    {
        if (!double.IsFinite(PrefetchMargin) || !double.IsFinite(RetentionMargin) ||
            PrefetchMargin < 0 || RetentionMargin < PrefetchMargin || RetentionTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(PrefetchMargin));
    }
}
