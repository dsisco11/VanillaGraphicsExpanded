using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Fixtures.WorldPartition;

/// <summary>Deterministic provider with immutable captured data, deferred callbacks, and reusable physical slots.</summary>
internal sealed class PartitionProviderFixture : IPartitionProvider
{
    /// <summary>Dependency revision captured without retaining a mutable source.</summary>
    internal sealed record Snapshot(long DependencyRevision) : IPartitionSnapshot;
    /// <summary>Independent immutable processing result.</summary>
    internal sealed record Content(long DependencyRevision) : IPartitionContent;
    /// <summary>Deferred work that can complete after cancellation to reproduce races.</summary>
    internal sealed record Work(PartitionRequest Request, IPartitionSnapshot Snapshot, Action<PartitionCompletion> Callback);
    public long DependencyRevision { get; set; } = 1;
    public bool Missing { get; set; }
    public bool Unsupported { get; set; }
    public bool AutoComplete { get; set; }
    public Action? DuringPublish { get; set; }
    public bool PublishSucceeds { get; set; } = true;
    public bool ActivationSucceeds { get; set; } = true;
    public long UploadBytes { get; set; } = 4;
    public List<(PartitionCellKey Key, bool Active)> Activations { get; } = new();
    public List<Work> Pending { get; } = new();
    public List<PartitionRequest> Captures { get; } = new();
    public List<PartitionCompletion> Publications { get; } = new();
    public List<int> CallbackThreads { get; } = new();
    public Dictionary<PartitionCellKey, int> Slots { get; } = new();
    public HashSet<PartitionCellKey> Visible { get; } = new();
    private readonly Queue<int> freeSlots = new();
    private int nextSlot;

    #region Provider acknowledgements
    /// <summary>Copies the dependency revision and records capture evidence.</summary>
    public PartitionCapture Capture(PartitionRequest request)
    {
        CallbackThreads.Add(Environment.CurrentManagedThreadId);
        Captures.Add(request);
        return new(Missing ? PartitionContentStatus.MissingDependencies : Unsupported ? PartitionContentStatus.Unsupported : PartitionContentStatus.Supported,
            Missing ? null : new Snapshot(DependencyRevision));
    }
    /// <summary>Defers work unless a scenario explicitly requests immediate completion.</summary>
    public void Dispatch(PartitionRequest request, IPartitionSnapshot snapshot, Action<PartitionCompletion> complete)
    {
        CallbackThreads.Add(Environment.CurrentManagedThreadId);
        Pending.Add(new(request, snapshot, complete));
        if (AutoComplete) Complete(Pending.Count - 1);
    }
    /// <summary>Detects changes between capture and publication.</summary>
    public bool DependenciesValid(PartitionRequest request, IPartitionSnapshot snapshot) => ((Snapshot)snapshot).DependencyRevision == DependencyRevision;
    /// <summary>Publishes all simulated resources together and records the logical slot owner.</summary>
    public bool Publish(PartitionCompletion completion)
    {
        CallbackThreads.Add(Environment.CurrentManagedThreadId);
        if (!PublishSucceeds) return false;
        if (!Slots.ContainsKey(completion.Request.Key)) Slots.Add(completion.Request.Key, freeSlots.Count > 0 ? freeSlots.Dequeue() : nextSlot++);
        Visible.Add(completion.Request.Key);
        Publications.Add(completion);
        DuringPublish?.Invoke();
        return true;
    }
    /// <summary>Allows activation acknowledgements to be delayed independently from content.</summary>
    public bool SetActive(in PartitionCellKey key, bool active)
    {
        Activations.Add((key, active));
        return ActivationSucceeds;
    }
    /// <summary>Immediately hides stale slot content.</summary>
    public void Invalidate(in PartitionCellKey key) => Visible.Remove(key);
    /// <summary>Returns retired storage for later logical owners.</summary>
    public void Retire(in PartitionCellKey key)
    {
        Visible.Remove(key);
        if (Slots.Remove(key, out int slot)) freeSlots.Enqueue(slot);
    }
    #endregion

    #region Scenario controls
    /// <summary>Completes any selected request, intentionally ignoring advisory cancellation.</summary>
    public void Complete(int index = 0)
    {
        Work work = Pending[index];
        Pending.RemoveAt(index);
        work.Callback(new(work.Request, work.Snapshot, new Content(((Snapshot)work.Snapshot).DependencyRevision), UploadBytes,
            Unsupported ? PartitionContentStatus.Unsupported : PartitionContentStatus.Supported));
    }
    /// <summary>Completes the currently dispatched batch without pumping or publishing.</summary>
    public void CompleteAll()
    {
        while (Pending.Count > 0) Complete();
    }
    #endregion
}
