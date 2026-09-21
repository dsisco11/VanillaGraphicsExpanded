using System;
using System.Threading;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Desired residency, independent of content validity.</summary>
internal enum PartitionResidency { Unloaded, Loaded, Active }
/// <summary>Observable progress of an acknowledged operation.</summary>
internal enum PartitionProgress { Idle, Capturing, Processing, AwaitingPublication, Retry, BudgetBlocked }
/// <summary>Content limitations are distinct from missing retryable dependencies.</summary>
internal enum PartitionContentStatus { Supported, Unsupported, MissingDependencies }

/// <summary>All identities needed to reject obsolete work, plus cooperative cancellation.</summary>
internal sealed record PartitionRequest(long Generation, PartitionCellKey Key, long Incarnation,
    long Revision, long RequestId, CancellationToken Cancellation);

/// <summary>Immutable provider-owned snapshot; implementations must not retain mutable world accessors.</summary>
internal interface IPartitionSnapshot { }
/// <summary>Immutable worker result; any backing storage stays immutable through publication.</summary>
internal interface IPartitionContent { }

/// <summary>Capture acknowledgement; missing dependencies carry no usable snapshot.</summary>
internal sealed record PartitionCapture(PartitionContentStatus Status, IPartitionSnapshot? Snapshot);
/// <summary>Worker acknowledgement safe to enqueue from any thread.</summary>
internal sealed record PartitionCompletion(PartitionRequest Request, IPartitionSnapshot Snapshot,
    IPartitionContent? Content, long UploadBytes, PartitionContentStatus Status);

/// <summary>Provider boundary; only Dispatch may initiate worker processing.</summary>
internal interface IPartitionProvider
{
    /// <summary>Captures on the owning thread under game-access constraints.</summary>
    PartitionCapture Capture(PartitionRequest request);
    /// <summary>Uses existing executors and acknowledges immutable results through complete.</summary>
    void Dispatch(PartitionRequest request, IPartitionSnapshot snapshot, Action<PartitionCompletion> complete);
    /// <summary>Revalidates snapshot dependencies immediately before publication.</summary>
    bool DependenciesValid(PartitionRequest request, IPartitionSnapshot snapshot);
    /// <summary>Uploads coherent resources without changing participation on the render/owning thread; false must leave content unavailable.</summary>
    bool Publish(PartitionCompletion completion);
    /// <summary>Acknowledges participation changes without a mandatory reupload.</summary>
    bool SetActive(in PartitionCellKey key, bool active);
    /// <summary>Makes dirty or departing contents inaccessible before reuse.</summary>
    void Invalidate(in PartitionCellKey key);
    /// <summary>Releases storage and participation on the render/owning thread, including uncaptured cells; delayed backends must defer reuse themselves.</summary>
    void Retire(in PartitionCellKey key);
}
