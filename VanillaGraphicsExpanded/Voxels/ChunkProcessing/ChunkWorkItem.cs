using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Profiling;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

internal sealed class ChunkWorkItem<TArtifact> : IChunkWorkItem
{
    private readonly ChunkKey key;
    private readonly int version;
    private readonly IChunkProcessor<TArtifact> processor;
    private readonly ChunkProcessingService snapshotLeaseProvider;
    private readonly IChunkVersionProvider versionProvider;
    private readonly CancellationToken callerCancellationToken;
    private readonly TaskCompletionSource<ChunkWorkResult<TArtifact>> tcs;
    private readonly ConcurrentDictionary<ArtifactKey, Task> inFlight;
    private readonly ConcurrentDictionary<ChunkProcessorKey, ConcurrentDictionary<int, IChunkWorkItem>> pendingByProcessorKey;

    public ChunkWorkItem(
        ChunkKey key,
        int version,
        IChunkProcessor<TArtifact> processor,
        ChunkProcessingService snapshotLeaseProvider,
        IChunkVersionProvider versionProvider,
        CancellationToken callerCancellationToken,
        TaskCompletionSource<ChunkWorkResult<TArtifact>> tcs,
        ConcurrentDictionary<ArtifactKey, Task> inFlight,
        ConcurrentDictionary<ChunkProcessorKey, ConcurrentDictionary<int, IChunkWorkItem>> pendingByProcessorKey)
    {
        this.key = key;
        this.version = version;
        this.processor = processor ?? throw new ArgumentNullException(nameof(processor));
        this.snapshotLeaseProvider = snapshotLeaseProvider ?? throw new ArgumentNullException(nameof(snapshotLeaseProvider));
        this.versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
        this.callerCancellationToken = callerCancellationToken;
        this.tcs = tcs ?? throw new ArgumentNullException(nameof(tcs));
        this.inFlight = inFlight ?? throw new ArgumentNullException(nameof(inFlight));
        this.pendingByProcessorKey = pendingByProcessorKey ?? throw new ArgumentNullException(nameof(pendingByProcessorKey));

        ArtifactKey = new ArtifactKey(key, version, processor.Id);
        ChunkProcessorKey = new ChunkProcessorKey(key, processor.Id);
    }

    public ArtifactKey ArtifactKey { get; }

    public ChunkProcessorKey ChunkProcessorKey { get; }

    public int Version => version;

    public bool TryCompleteSuperseded(string reason)
    {
        bool completed = tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
            Status: ChunkWorkStatus.Superseded,
            Key: key,
            RequestedVersion: version,
            ProcessorId: processor.Id,
            Artifact: default,
            Error: ChunkWorkError.None,
            Reason: reason));

        if (completed)
        {
            if (inFlight.TryRemove(ArtifactKey, out _))
            {
                ChunkProcessingMetrics.OnInFlightRemoved();
            }

            ChunkProcessingMetrics.OnCompleted(ChunkWorkStatus.Superseded);
        }

        return completed;
    }

    public async Task ExecuteAsync(CancellationToken serviceCancellationToken)
    {
        try
        {
            if (tcs.Task.IsCompleted)
            {
                return;
            }

            if (pendingByProcessorKey.TryGetValue(ChunkProcessorKey, out ConcurrentDictionary<int, IChunkWorkItem>? pending)
                && pending.TryRemove(version, out _)
                && pending.IsEmpty)
            {
                pendingByProcessorKey.TryRemove(ChunkProcessorKey, out _);
            }

            if (callerCancellationToken.IsCancellationRequested)
            {
                if (tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Canceled,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.None,
                    Reason: "Canceled")))
                {
                    ChunkProcessingMetrics.OnCompleted(ChunkWorkStatus.Canceled);
                }

                return;
            }

            if (versionProvider.GetCurrentVersion(key) != version)
            {
                if (tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Superseded,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.None,
                    Reason: "Superseded")))
                {
                    ChunkProcessingMetrics.OnCompleted(ChunkWorkStatus.Superseded);
                }

                return;
            }

            using var scope = Profiler.BeginScope(string.Concat("ChunkProc.", processor.Id), "ChunkProcessing");

            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serviceCancellationToken, callerCancellationToken);
            CancellationToken ct = linkedCts.Token;

            IChunkSnapshot? snapshot;
            try
            {
                snapshot = await snapshotLeaseProvider.TryAcquireSnapshotLeaseAsync(key, version, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Canceled,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.None,
                    Reason: "Canceled")))
                {
                    ChunkProcessingMetrics.OnCompleted(ChunkWorkStatus.Canceled);
                }

                return;
            }
            catch (Exception ex)
            {
                if (tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Failed,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.SnapshotFailed,
                    Reason: ex.Message)))
                {
                    ChunkProcessingMetrics.OnCompleted(ChunkWorkStatus.Failed);
                }

                return;
            }

            if (snapshot is null)
            {
                if (tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.ChunkUnavailable,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.None,
                    Reason: "ChunkUnavailable")))
                {
                    ChunkProcessingMetrics.OnCompleted(ChunkWorkStatus.ChunkUnavailable);
                }

                return;
            }

            ChunkWorkResult<TArtifact>? finalResult = null;

            using (snapshot)
            {
                TArtifact artifact;
                try
                {
                    artifact = await processor.ProcessAsync(snapshot, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    finalResult = new ChunkWorkResult<TArtifact>(
                        Status: ChunkWorkStatus.Canceled,
                        Key: key,
                        RequestedVersion: version,
                        ProcessorId: processor.Id,
                        Artifact: default,
                        Error: ChunkWorkError.None,
                        Reason: "Canceled");

                    goto CompleteAfterDispose;
                }
                catch (Exception ex)
                {
                    finalResult = new ChunkWorkResult<TArtifact>(
                        Status: ChunkWorkStatus.Failed,
                        Key: key,
                        RequestedVersion: version,
                        ProcessorId: processor.Id,
                        Artifact: default,
                        Error: ChunkWorkError.ProcessorFailed,
                        Reason: ex.Message);

                    goto CompleteAfterDispose;
                }

                if (versionProvider.GetCurrentVersion(key) != version)
                {
                    finalResult = new ChunkWorkResult<TArtifact>(
                        Status: ChunkWorkStatus.Superseded,
                        Key: key,
                        RequestedVersion: version,
                        ProcessorId: processor.Id,
                        Artifact: default,
                        Error: ChunkWorkError.None,
                        Reason: "Superseded");

                    goto CompleteAfterDispose;
                }

                snapshotLeaseProvider.CacheArtifact(ArtifactKey, artifact!);

                finalResult = new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Success,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: artifact,
                    Error: ChunkWorkError.None,
                    Reason: null);
            }

CompleteAfterDispose:

            if (finalResult is not null)
            {
                if (tcs.TrySetResult(finalResult.Value))
                {
                    ChunkProcessingMetrics.OnCompleted(finalResult.Value.Status);
                }
            }
        }
        finally
        {
            if (inFlight.TryRemove(ArtifactKey, out _))
            {
                ChunkProcessingMetrics.OnInFlightRemoved();
            }

            if (pendingByProcessorKey.TryGetValue(ChunkProcessorKey, out ConcurrentDictionary<int, IChunkWorkItem>? pending)
                && pending.TryRemove(version, out _)
                && pending.IsEmpty)
            {
                pendingByProcessorKey.TryRemove(ChunkProcessorKey, out _);
            }
        }
    }
}
