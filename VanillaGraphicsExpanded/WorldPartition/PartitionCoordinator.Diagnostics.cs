using System.Linq;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Render-thread snapshots for diagnostics without exposing mutable registration state.</summary>
internal sealed partial class PartitionCoordinator
{
    #region Diagnostic snapshots
    /// <summary>Captures all registrations, including their source envelopes and acknowledged cell readiness.</summary>
    public PartitionDiagnosticSnapshot[] Diagnostics()
    {
        CheckOwner();
        return registrations.Values.Select(r => new PartitionDiagnosticSnapshot(r.Instance, r.Name, r.World,
            r.Layout, r.Limits, r.Sources.Values.ToArray(), Cells(r.Instance), Statistics(r.Instance),
            r.Cells.Values.Count(c => c.Desired == PartitionResidency.Active && c.Ready),
            r.Cells.Values.Where(c => c.Desired == PartitionResidency.Active && !c.Ready)
                .Select(c => tick - c.UnreadySince).DefaultIfEmpty().Max(), r.PublicationCount,
            r.PublicationCount == 0 ? 0 : (double)r.PublicationWaitTicks / r.PublicationCount,
            r.MaximumPublicationWaitTicks)).ToArray();
    }
    #endregion
}

/// <summary>Detached diagnostic data; ready-required counts exclude speculative cells.</summary>
internal sealed record PartitionDiagnosticSnapshot(long Instance, string Name, string World,
    PartitionLayout Layout, PartitionLimits Limits, PartitionSource[] Sources, PartitionCellInfo[] Cells,
    PartitionStatistics Statistics, int RequiredReady, long OldestRequiredWaitTicks, long PublicationCount,
    double MeanPublicationWaitTicks, long MaximumPublicationWaitTicks);
