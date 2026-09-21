using System.Collections.Generic;
using System.Threading;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Coordinator-owned mutable state, never exposed to worker providers.</summary>
internal sealed class PartitionCellState
{
    public required PartitionCellKey Key;
    public required long Incarnation;
    public long Revision = 1;
    public PartitionResidency Desired;
    public PartitionResidency Actual;
    public PartitionProgress Progress;
    public PartitionContentStatus ContentStatus;
    public bool Ready;
    public bool Reserved;
    public long LastRequested;
    public long WaitingSince;
    public long RetryAt;
    public long RequiredUploadBytes;
    public readonly HashSet<long> RequiredSources = new();
    public readonly HashSet<long> PrefetchSources = new();
    public PartitionRequest? Request;
    public CancellationTokenSource? Cancellation;
    public IPartitionSnapshot? Snapshot;
    public PartitionCompletion? Completion;
}
