using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

public sealed class ChunkProcessingService : IChunkProcessingService, IDisposable
{
    private readonly IChunkSnapshotSource snapshotSource;
    private readonly IChunkVersionProvider versionProvider;

    private readonly ConcurrentDictionary<SnapshotKey, SharedSnapshotEntry> snapshots = new();

    private long snapshotBytesInUse;

    private readonly Channel<IChunkWorkItem> work;

    private readonly ConcurrentDictionary<ArtifactKey, Task> inFlight = new();

    private readonly ConcurrentDictionary<ChunkProcessorKey, int> latestRequestedVersion = new();

    private readonly ConcurrentDictionary<ChunkProcessorKey, ConcurrentDictionary<int, IChunkWorkItem>> pendingByProcessorKey = new();

    private readonly CancellationTokenSource cts = new();

    private readonly Task[] workers;

    private readonly TimeSpan shutdownTimeout;

    public ChunkProcessingService(
        IChunkSnapshotSource snapshotSource,
        IChunkVersionProvider versionProvider,
        ChunkProcessingServiceOptions? options = null)
    {
        this.snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
        this.versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));

        options ??= new ChunkProcessingServiceOptions();

        shutdownTimeout = options.ShutdownTimeout;

        var channelOptions = new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        };

        work = Channel.CreateUnbounded<IChunkWorkItem>(channelOptions);

        int workerCount = Math.Max(1, options.WorkerCount);
        workers = new Task[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            workers[i] = Task.Run(WorkerLoop);
        }
    }

    internal long SnapshotBytesInUse => Interlocked.Read(ref snapshotBytesInUse);

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

        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(new ChunkWorkResult<TArtifact>(
                Status: ChunkWorkStatus.Canceled,
                Key: key,
                RequestedVersion: version,
                ProcessorId: processor.Id,
                Artifact: default,
                Error: ChunkWorkError.None,
                Reason: "Canceled"));
        }

        var processorKey = new ChunkProcessorKey(key, processor.Id);

        // Phase 2: If a newer request has already been submitted for this (chunk, processor), this request is superseded immediately.
        if (latestRequestedVersion.TryGetValue(processorKey, out int latest) && version < latest)
        {
            return Task.FromResult(new ChunkWorkResult<TArtifact>(
                Status: ChunkWorkStatus.Superseded,
                Key: key,
                RequestedVersion: version,
                ProcessorId: processor.Id,
                Artifact: default,
                Error: ChunkWorkError.None,
                Reason: "Superseded"));
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
            return Task.FromResult(new ChunkWorkResult<TArtifact>(
                Status: ChunkWorkStatus.Superseded,
                Key: key,
                RequestedVersion: version,
                ProcessorId: processor.Id,
                Artifact: default,
                Error: ChunkWorkError.None,
                Reason: "Superseded"));
        }

        var artifactKey = new ArtifactKey(key, version, processor.Id);

        while (true)
        {
            if (inFlight.TryGetValue(artifactKey, out Task? existing))
            {
                if (existing is Task<ChunkWorkResult<TArtifact>> typed)
                {
                    return typed;
                }

                return Task.FromResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Failed,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.Unknown,
                    Reason: "ArtifactKeyTypeMismatch"));
            }

            var tcs = new TaskCompletionSource<ChunkWorkResult<TArtifact>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = tcs.Task;

            if (!inFlight.TryAdd(artifactKey, task))
            {
                continue;
            }

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

            if (!work.Writer.TryWrite(workItem))
            {
                inFlight.TryRemove(artifactKey, out _);

                if (pendingByProcessorKey.TryGetValue(processorKey, out ConcurrentDictionary<int, IChunkWorkItem>? pending)
                    && pending.TryRemove(version, out _)
                    && pending.IsEmpty)
                {
                    pendingByProcessorKey.TryRemove(processorKey, out _);
                }

                return Task.FromResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Failed,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.Unknown,
                    Reason: "ServiceUnavailable"));
            }

            return task;
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        work.Writer.TryComplete();

        try
        {
            Task.WaitAll(workers, shutdownTimeout);
        }
        catch
        {
            // Best-effort shutdown.
        }

        cts.Dispose();
    }

    private async Task WorkerLoop()
    {
        try
        {
            while (await work.Reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
            {
                while (work.Reader.TryRead(out IChunkWorkItem? item))
                {
                    try
                    {
                        await item.ExecuteAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Work items must swallow and report failures via their result wrappers.
                        // This catch ensures a single misbehaving work item can't kill the worker loop.
                    }
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
                onBytesAdd = (Action<long>)(b => Interlocked.Add(ref snapshotBytesInUse, b)),
                onBytesRemove = (Action<long>)(b => Interlocked.Add(ref snapshotBytesInUse, -b)),
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
}
