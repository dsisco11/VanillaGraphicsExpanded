using System;
using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Cache.Artifacts;

internal enum ArtifactEnqueueResult
{
    SkippedUpToDate,
    Enqueued,
    AlreadyQueued,
}

internal static class ArtifactEnqueueHelper
{
    public static ArtifactEnqueueResult EnqueueIfStale<TKey>(
        IArtifactScheduler<TKey> scheduler,
        TKey key,
        Func<TKey, bool> isUpToDate,
        Func<TKey, IArtifactWorkItem<TKey>> createItem)
    {
        if (scheduler is null) throw new ArgumentNullException(nameof(scheduler));
        if (isUpToDate is null) throw new ArgumentNullException(nameof(isUpToDate));
        if (createItem is null) throw new ArgumentNullException(nameof(createItem));

        if (isUpToDate(key))
        {
            return ArtifactEnqueueResult.SkippedUpToDate;
        }

        return scheduler.Enqueue(createItem(key))
            ? ArtifactEnqueueResult.Enqueued
            : ArtifactEnqueueResult.AlreadyQueued;
    }

    public static async ValueTask<ArtifactEnqueueResult> EnqueueIfStaleAsync<TKey>(
        IArtifactScheduler<TKey> scheduler,
        TKey key,
        Func<TKey, CancellationToken, ValueTask<bool>> isUpToDate,
        Func<TKey, IArtifactWorkItem<TKey>> createItem,
        CancellationToken cancellationToken = default)
    {
        if (scheduler is null) throw new ArgumentNullException(nameof(scheduler));
        if (isUpToDate is null) throw new ArgumentNullException(nameof(isUpToDate));
        if (createItem is null) throw new ArgumentNullException(nameof(createItem));

        if (await isUpToDate(key, cancellationToken).ConfigureAwait(false))
        {
            return ArtifactEnqueueResult.SkippedUpToDate;
        }

        return scheduler.Enqueue(createItem(key))
            ? ArtifactEnqueueResult.Enqueued
            : ArtifactEnqueueResult.AlreadyQueued;
    }
}
