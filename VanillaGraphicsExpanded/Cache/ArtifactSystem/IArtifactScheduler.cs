namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

/// <summary>
/// Scheduler contract for continuously executing cache artifact work.
/// Concrete implementations own queueing, deduplication, sessioning, and backpressure.
/// </summary>
internal interface IArtifactScheduler<TKey>
{
    void Start();

    void Stop();

    /// <summary>
    /// Enqueue a work item. Returns false if an item with the same key is already queued.
    /// </summary>
    bool Enqueue(IArtifactWorkItem<TKey> item);

    /// <summary>
    /// Bumps the session id and cancels prior in-flight work.
    /// Intended for reloads.
    /// </summary>
    void BumpSession();

    ArtifactSchedulerStats GetStatsSnapshot();
}
