using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

public sealed class ChunkProcessingService : IChunkProcessingService, IDisposable
{
    private readonly IChunkSnapshotSource snapshotSource;
    private readonly IChunkVersionProvider versionProvider;

    private readonly ArtifactCache artifactCache;

    private readonly ConcurrentDictionary<SnapshotKey, SharedSnapshotEntry> snapshots = new();

    private long snapshotBytesInUse;

    private readonly Channel<QueuedWorkItem> workChannel;
    private long queueSeq;

    private readonly object readyGate = new();
    private readonly PriorityQueue<IChunkWorkItem, (int negPriority, long seq)> readyQueue = new();
    private readonly SemaphoreSlim readyAvailable = new(0);
    private readonly Task pumpTask;

    private readonly ConcurrentDictionary<ArtifactKey, Task> inFlight = new();

    private readonly ConcurrentDictionary<ChunkProcessorKey, int> latestRequestedVersion = new();

    private readonly ConcurrentDictionary<ChunkProcessorKey, ConcurrentDictionary<int, IChunkWorkItem>> pendingByProcessorKey = new();

    private readonly CancellationTokenSource cts = new();

    private readonly Task[] workers;

    private readonly TimeSpan shutdownTimeout;

    private int disposed;

    public ChunkProcessingService(
        IChunkSnapshotSource snapshotSource,
        IChunkVersionProvider versionProvider,
        ChunkProcessingServiceOptions? options = null)
    {
        this.snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
        this.versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));

        options ??= new ChunkProcessingServiceOptions();

        artifactCache = new ArtifactCache(options.ArtifactCacheBudgetBytes);

        shutdownTimeout = options.ShutdownTimeout;

        workChannel = Channel.CreateUnbounded<QueuedWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        pumpTask = Task.Run(PumpLoop);

        int workerCount = Math.Max(1, options.WorkerCount);
        workers = new Task[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            workers[i] = Task.Run(WorkerLoop);
        }
    }

    internal long SnapshotBytesInUse => Interlocked.Read(ref snapshotBytesInUse);

    internal long ArtifactCacheBytesInUse => artifactCache.BytesInUse;

    public Task<ChunkWorkResult<TArtifact>> RequestAsync<TArtifact>(
        ChunkKey key,
        int version,
        IChunkProcessor<TArtifact> processor,
        ChunkWorkOptions? options = null,
        CancellationToken ct = default)
    {
        if (processor is null)
        {
            throw new ArgumentNullException(nameof(processor));
        }

        if (Volatile.Read(ref disposed) != 0 || cts.IsCancellationRequested)
        {
            var unavailable = new ChunkWorkResult<TArtifact>(
                Status: ChunkWorkStatus.Failed,
                Key: key,
                RequestedVersion: version,
                ProcessorId: processor.Id,
                Artifact: default,
                Error: ChunkWorkError.Unknown,
                Reason: "ServiceUnavailable");

            ChunkProcessingMetrics.OnCompleted(unavailable.Status);
            return Task.FromResult(unavailable);
        }

        if (ct.IsCancellationRequested)
        {
            var res = new ChunkWorkResult<TArtifact>(
                Status: ChunkWorkStatus.Canceled,
                Key: key,
                RequestedVersion: version,
                ProcessorId: processor.Id,
                Artifact: default,
                Error: ChunkWorkError.None,
                Reason: "Canceled");

            ChunkProcessingMetrics.OnCompleted(res.Status);
            return Task.FromResult(res);
        }

        var processorKey = new ChunkProcessorKey(key, processor.Id);

        // Phase 2: If a newer request has already been submitted for this (chunk, processor), this request is superseded immediately.
        if (latestRequestedVersion.TryGetValue(processorKey, out int latest) && version < latest)
        {
            var res = new ChunkWorkResult<TArtifact>(
                Status: ChunkWorkStatus.Superseded,
                Key: key,
                RequestedVersion: version,
                ProcessorId: processor.Id,
                Artifact: default,
                Error: ChunkWorkError.None,
                Reason: "Superseded");

            ChunkProcessingMetrics.OnCompleted(res.Status);
            return Task.FromResult(res);
        }

        // Update "latest requested" (monotonic max).
        while (true)
        {
            if (!latestRequestedVersion.TryGetValue(processorKey, out latest))
            {
                if (latestRequestedVersion.TryAdd(processorKey, version))
                {
                    latest = version;
                    break;
                }

                continue;
            }

            if (version <= latest)
            {
                break;
            }

            if (latestRequestedVersion.TryUpdate(processorKey, version, latest))
            {
                latest = version;
                break;
            }
        }

        // If we lost the race and are now older than the latest, complete immediately.
        if (version < latest)
        {
            var res = new ChunkWorkResult<TArtifact>(
                Status: ChunkWorkStatus.Superseded,
                Key: key,
                RequestedVersion: version,
                ProcessorId: processor.Id,
                Artifact: default,
                Error: ChunkWorkError.None,
                Reason: "Superseded");

            ChunkProcessingMetrics.OnCompleted(res.Status);
            return Task.FromResult(res);
        }

        // Cache hit fast-path (still guarded by current-version).
        if (versionProvider.GetCurrentVersion(key) != version)
        {
            var res = new ChunkWorkResult<TArtifact>(
                Status: ChunkWorkStatus.Superseded,
                Key: key,
                RequestedVersion: version,
                ProcessorId: processor.Id,
                Artifact: default,
                Error: ChunkWorkError.None,
                Reason: "Superseded");

            ChunkProcessingMetrics.OnCompleted(res.Status);
            return Task.FromResult(res);
        }

        var artifactKey = new ArtifactKey(key, version, processor.Id);

        if (TryGetCachedArtifact(artifactKey, out TArtifact? cached, out string? cacheErrorReason))
        {
            if (cacheErrorReason is not null)
            {
                var res = new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Failed,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.Unknown,
                    Reason: cacheErrorReason);

                ChunkProcessingMetrics.OnCompleted(res.Status);
                return Task.FromResult(res);
            }

            var hit = new ChunkWorkResult<TArtifact>(
                Status: ChunkWorkStatus.Success,
                Key: key,
                RequestedVersion: version,
                ProcessorId: processor.Id,
                Artifact: cached,
                Error: ChunkWorkError.None,
                Reason: null);

            ChunkProcessingMetrics.OnCompleted(hit.Status);
            return Task.FromResult(hit);
        }

        while (true)
        {
            if (inFlight.TryGetValue(artifactKey, out Task? existing))
            {
                if (existing is Task<ChunkWorkResult<TArtifact>> typed)
                {
                    return typed;
                }

                var mismatch = new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Failed,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.Unknown,
                    Reason: "ArtifactKeyTypeMismatch");

                ChunkProcessingMetrics.OnCompleted(mismatch.Status);
                return Task.FromResult(mismatch);
            }

            var tcs = new TaskCompletionSource<ChunkWorkResult<TArtifact>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = tcs.Task;

            if (!inFlight.TryAdd(artifactKey, task))
            {
                continue;
            }

            ChunkProcessingMetrics.OnInFlightAdded();

            var workItem = new ChunkWorkItem<TArtifact>(
                key: key,
                version: version,
                processor: processor,
                snapshotLeaseProvider: this,
                versionProvider: versionProvider,
                callerCancellationToken: ct,
                tcs: tcs,
                inFlight: inFlight,
                pendingByProcessorKey: pendingByProcessorKey);

            ConcurrentDictionary<int, IChunkWorkItem> pendingByVersion = pendingByProcessorKey.GetOrAdd(
                processorKey,
                static _ => new ConcurrentDictionary<int, IChunkWorkItem>());

            // No duplicate compute per (ChunkKey, Version, ProcessorId) means this should normally succeed.
            pendingByVersion.TryAdd(version, workItem);

            // Phase 2: when a new latest version arrives, eagerly supersede any older queued versions.
            if (pendingByVersion.Count > 1)
            {
                foreach (var kvp in pendingByVersion)
                {
                    if (kvp.Key >= version)
                    {
                        continue;
                    }

                    if (!pendingByVersion.TryRemove(kvp.Key, out IChunkWorkItem? older))
                    {
                        continue;
                    }

                    older.TryCompleteSuperseded("Superseded");
                }

                if (pendingByVersion.IsEmpty)
                {
                    pendingByProcessorKey.TryRemove(processorKey, out _);
                }
            }

            EnqueueWorkItem(workItem, options?.Priority ?? 0);

            ChunkProcessingMetrics.OnEnqueued();

            return task;
        }
    }

    private void EnqueueWorkItem(IChunkWorkItem item, int priority)
    {
        long seq = Interlocked.Increment(ref queueSeq);
        var queued = new QueuedWorkItem(item, priority, seq);

        // Unbounded channel: TryWrite should always succeed unless completed/disposed.
        if (!workChannel.Writer.TryWrite(queued))
        {
            item.TryCompleteSuperseded("ServiceUnavailable");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        _ = workChannel.Writer.TryComplete();
        cts.Cancel();

        try
        {
            pumpTask.Wait(shutdownTimeout);
            Task.WaitAll(workers, shutdownTimeout);
        }
        catch
        {
            // Best-effort shutdown.
        }

        readyAvailable.Dispose();
        cts.Dispose();
    }

    private async Task PumpLoop()
    {
        try
        {
            await foreach (QueuedWorkItem queued in workChannel.Reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
            {
                int negPriority = unchecked(-queued.Priority);

                lock (readyGate)
                {
                    readyQueue.Enqueue(queued.Item, (negPriority, queued.Seq));
                }

                readyAvailable.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch
        {
            // Swallow for now; higher-level integration will add logging.
        }
    }

    private async Task WorkerLoop()
    {
        try
        {
            while (true)
            {
                await readyAvailable.WaitAsync(cts.Token).ConfigureAwait(false);

                IChunkWorkItem? item = null;
                lock (readyGate)
                {
                    if (readyQueue.Count > 0)
                    {
                        item = readyQueue.Dequeue();
                    }
                }

                if (item is null)
                {
                    continue;
                }

                try
                {
                    ChunkProcessingMetrics.OnDequeued();
                    await item.ExecuteAsync(cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Work items must swallow and report failures via their result wrappers.
                    // This catch ensures a single misbehaving work item can't kill the worker loop.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch
        {
            // Swallow for now; higher-level integration will add logging.
        }
    }

    private readonly record struct QueuedWorkItem(IChunkWorkItem Item, int Priority, long Seq);

    internal ValueTask<IChunkSnapshot?> TryAcquireSnapshotLeaseAsync(ChunkKey key, int version, CancellationToken ct)
    {
        return TryAcquireSnapshotLeaseSlowAsync(new SnapshotKey(key, version), ct);
    }

    private async ValueTask<IChunkSnapshot?> TryAcquireSnapshotLeaseSlowAsync(SnapshotKey snapshotKey, CancellationToken ct)
    {
        SharedSnapshotEntry entry = snapshots.GetOrAdd(
            snapshotKey,
            static (k, state) => new SharedSnapshotEntry(
                key: k,
                snapshotFactory: () => state.snapshotSource.TryCreateSnapshotAsync(k.Key, k.Version, state.serviceCtsToken).AsTask(),
                onSnapshotBytesAdd: state.onBytesAdd,
                onSnapshotBytesRemove: state.onBytesRemove),
            new
            {
                snapshotSource,
                serviceCtsToken = cts.Token,
                onBytesAdd = (Action<long>)(b =>
                {
                    Interlocked.Add(ref snapshotBytesInUse, b);
                    ChunkProcessingMetrics.OnSnapshotBytesDelta(b);
                }),
                onBytesRemove = (Action<long>)(b =>
                {
                    Interlocked.Add(ref snapshotBytesInUse, -b);
                    ChunkProcessingMetrics.OnSnapshotBytesDelta(-b);
                }),
            });

        IChunkSnapshot? snapshot;
        try
        {
            snapshot = await entry.GetOrAwaitSnapshotAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Don't remove entry here: another waiter may still use it.
            throw;
        }

        if (snapshot is null)
        {
            snapshots.TryRemove(snapshotKey, out _);
            return null;
        }

        entry.AddRef();
        return new SharedSnapshotLease(snapshot, release: () => ReleaseSnapshot(snapshotKey, entry));
    }

    private void ReleaseSnapshot(SnapshotKey snapshotKey, SharedSnapshotEntry entry)
    {
        if (entry.ReleaseAndMaybeDispose() != 0)
        {
            return;
        }

        snapshots.TryRemove(snapshotKey, out _);
    }

    internal void CacheArtifact(ArtifactKey key, object artifact)
    {
        artifactCache.Put(key, artifact);
    }

    private bool TryGetCachedArtifact<TArtifact>(ArtifactKey key, out TArtifact? artifact, out string? errorReason)
    {
        if (!artifactCache.TryGet(key, out object? boxed))
        {
            artifact = default;
            errorReason = null;
            return false;
        }

        if (boxed is TArtifact typed)
        {
            artifact = typed;
            errorReason = null;
            return true;
        }

        artifact = default;
        errorReason = "ArtifactCacheTypeMismatch";
        return true;
    }
}
