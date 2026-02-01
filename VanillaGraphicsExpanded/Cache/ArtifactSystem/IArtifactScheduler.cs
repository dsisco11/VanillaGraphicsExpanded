using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>
    /// Drains all queued work items synchronously on the current thread until the scheduler becomes idle.
    /// This is intended for loading-screen workflows where background scheduling and main-thread apply callbacks
    /// would otherwise cause post-load work or deadlocks.
    /// </summary>
    void FinishOnCurrentThread(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously waits until the scheduler becomes idle (no queued items, no in-flight work,
    /// and no pending apply callbacks).
    /// </summary>
    Task WaitForIdleAsync(CancellationToken cancellationToken = default);

    ArtifactSchedulerStats GetStatsSnapshot();
}
