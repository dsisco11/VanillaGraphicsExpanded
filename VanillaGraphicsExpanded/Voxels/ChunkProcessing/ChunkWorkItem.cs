using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

internal sealed class ChunkWorkItem<TArtifact> : IChunkWorkItem
{
    private readonly ChunkKey key;
    private readonly int version;
    private readonly IChunkProcessor<TArtifact> processor;
    private readonly IChunkSnapshotSource snapshotSource;
    private readonly IChunkVersionProvider versionProvider;
    private readonly CancellationToken callerCancellationToken;
    private readonly TaskCompletionSource<ChunkWorkResult<TArtifact>> tcs;
    private readonly ConcurrentDictionary<ArtifactKey, Task> inFlight;

    public ChunkWorkItem(
        ChunkKey key,
        int version,
        IChunkProcessor<TArtifact> processor,
        IChunkSnapshotSource snapshotSource,
        IChunkVersionProvider versionProvider,
        CancellationToken callerCancellationToken,
        TaskCompletionSource<ChunkWorkResult<TArtifact>> tcs,
        ConcurrentDictionary<ArtifactKey, Task> inFlight)
    {
        this.key = key;
        this.version = version;
        this.processor = processor ?? throw new ArgumentNullException(nameof(processor));
        this.snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
        this.versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
        this.callerCancellationToken = callerCancellationToken;
        this.tcs = tcs ?? throw new ArgumentNullException(nameof(tcs));
        this.inFlight = inFlight ?? throw new ArgumentNullException(nameof(inFlight));

        ArtifactKey = new ArtifactKey(key, version, processor.Id);
    }

    public ArtifactKey ArtifactKey { get; }

    public async Task ExecuteAsync(CancellationToken serviceCancellationToken)
    {
        try
        {
            if (callerCancellationToken.IsCancellationRequested)
            {
                tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Canceled,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.None,
                    Reason: "Canceled"));

                return;
            }

            if (versionProvider.GetCurrentVersion(key) != version)
            {
                tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Superseded,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.None,
                    Reason: "Superseded"));

                return;
            }

            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serviceCancellationToken, callerCancellationToken);
            CancellationToken ct = linkedCts.Token;

            IChunkSnapshot? snapshot;
            try
            {
                snapshot = await snapshotSource.TryCreateSnapshotAsync(key, version, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Canceled,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.None,
                    Reason: "Canceled"));

                return;
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Failed,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.SnapshotFailed,
                    Reason: ex.Message));

                return;
            }

            if (snapshot is null)
            {
                tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.ChunkUnavailable,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: default,
                    Error: ChunkWorkError.None,
                    Reason: "ChunkUnavailable"));

                return;
            }

            using (snapshot)
            {
                TArtifact artifact;
                try
                {
                    artifact = await processor.ProcessAsync(snapshot, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                        Status: ChunkWorkStatus.Canceled,
                        Key: key,
                        RequestedVersion: version,
                        ProcessorId: processor.Id,
                        Artifact: default,
                        Error: ChunkWorkError.None,
                        Reason: "Canceled"));

                    return;
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                        Status: ChunkWorkStatus.Failed,
                        Key: key,
                        RequestedVersion: version,
                        ProcessorId: processor.Id,
                        Artifact: default,
                        Error: ChunkWorkError.ProcessorFailed,
                        Reason: ex.Message));

                    return;
                }

                if (versionProvider.GetCurrentVersion(key) != version)
                {
                    tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                        Status: ChunkWorkStatus.Superseded,
                        Key: key,
                        RequestedVersion: version,
                        ProcessorId: processor.Id,
                        Artifact: default,
                        Error: ChunkWorkError.None,
                        Reason: "Superseded"));

                    return;
                }

                tcs.TrySetResult(new ChunkWorkResult<TArtifact>(
                    Status: ChunkWorkStatus.Success,
                    Key: key,
                    RequestedVersion: version,
                    ProcessorId: processor.Id,
                    Artifact: artifact,
                    Error: ChunkWorkError.None,
                    Reason: null));
            }
        }
        finally
        {
            inFlight.TryRemove(ArtifactKey, out _);
        }
    }
}
