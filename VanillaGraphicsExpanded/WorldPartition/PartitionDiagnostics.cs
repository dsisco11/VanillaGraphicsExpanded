namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Read-only cell observation without provider-owned payloads.</summary>
internal sealed record PartitionCellInfo(PartitionCellKey Key, long Incarnation, long Revision,
    PartitionResidency Desired, PartitionResidency Actual, PartitionProgress Progress,
    PartitionContentStatus ContentStatus, bool Ready);

/// <summary>Coverage and work counters; missing capacity never removes desired required cells.</summary>
internal sealed record PartitionStatistics(int Required, int Resident, int Ready, int Dirty, int Queued,
    int InFlight, int CaptureBacklog, int UploadBacklog, long Retries, long StaleCompletions,
    int CapacityShortfall, long CoverageEvaluations, long CoverageCellVisits, long UploadBudgetShortfall);
