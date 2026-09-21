using System.Collections.Generic;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Registration configuration and its private coverage/work state.</summary>
internal sealed class PartitionRegistration
{
    public required long Instance;
    public required long Generation;
    public required string Name;
    public required string World;
    public required PartitionLayout Layout;
    public required PartitionCoveragePolicy Coverage;
    public required PartitionLimits Limits;
    public required IPartitionProvider Provider;
    public readonly Dictionary<PartitionCoordinate, PartitionCellState> Cells = new();
    public readonly Dictionary<long, PartitionSource> Sources = new();
    public readonly Dictionary<long, PartitionSourceCoverage> SourceCoverage = new();
    public readonly HashSet<PartitionCoordinate> Retained = new();
    public long CoverageCellVisits;
    public bool CoverageDirty = true;
    public long CoverageEvaluations;
    public long Retries;
    public long StaleCompletions;
    public int Captures;
    public int Dispatches;
    public long Uploaded;
}
