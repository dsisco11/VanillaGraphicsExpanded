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

    private readonly Channel<IChunkWorkItem> work;

    private readonly ConcurrentDictionary<ArtifactKey, Task> inFlight = new();

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
                snapshotSource: snapshotSource,
                versionProvider: versionProvider,
                callerCancellationToken: ct,
                tcs: tcs,
                inFlight: inFlight);

            if (!work.Writer.TryWrite(workItem))
            {
                inFlight.TryRemove(artifactKey, out _);

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
}
