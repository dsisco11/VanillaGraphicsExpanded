using System.Collections.Immutable;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Profiling;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>Owns bounded CPU probe admissions, asynchronous tracing and non-consuming completion observation.</summary>
internal sealed class LumOnWorldProbeTraceService : IWorldProbeTraceBackend
{
    private readonly Channel<LumOnWorldProbeTraceWorkItem> work;

    private readonly Channel<LumOnWorldProbeTraceResult> results;

    private int approxQueuedWorkItems;
    private int approxQueuedResults;

    private readonly CancellationTokenSource cts = new();

    private readonly Task workerTask;

    private readonly IWorldProbeTraceScene scene;

    private readonly Func<LumOnWorldProbeUpdateRequest, int, bool> tryClaim;

    private readonly LumOnWorldProbeTraceIntegrator integrator = new();
    private readonly object progressGate = new();
    private int outstanding;
    private bool stopped;
    private TaskCompletionSource? progress;

    #region Progress observation
    /// <summary>Includes queued and actively tracing work, excluding completions already available to the consumer.</summary>
    internal bool HasOutstandingWork { get { lock (progressGate) return !stopped && outstanding > 0; } }

    /// <summary>Observes available results or idle/shutdown without consuming a producer-owned result.</summary>
    internal Task WaitForProgressAsync(CancellationToken cancellationToken)
    {
        lock (progressGate)
            return stopped || outstanding == 0 || results.Reader.TryPeek(out _)
                ? Task.CompletedTask : (progress ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(cancellationToken);
    }

    /// <summary>Finishes one admission and wakes observers even when its stale claim produced no result.</summary>
    private void CompleteAdmission()
    {
        lock (progressGate)
        {
            outstanding--;
            var previous = progress;
            progress = null;
            previous?.TrySetResult();
        }
    }

    #endregion

    public LumOnWorldProbeTraceService(
        IWorldProbeTraceScene scene,
        int maxQueuedWorkItems,
        Func<LumOnWorldProbeUpdateRequest, int, bool> tryClaim)
    {
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
        this.tryClaim = tryClaim ?? throw new ArgumentNullException(nameof(tryClaim));

        var workOpts = new BoundedChannelOptions(Math.Max(1, maxQueuedWorkItems))
        {
            // IMPORTANT: Claim-based scheduling means dropped work items no longer create permanent "InFlight" zombies.
            // We still prefer backpressure here so we don't waste work generating/queuing items that won't be processed.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        };

        work = Channel.CreateBounded<LumOnWorldProbeTraceWorkItem>(workOpts);
        results = Channel.CreateUnbounded<LumOnWorldProbeTraceResult>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });

        workerTask = Task.Run(WorkerLoop);
    }

    public bool TryEnqueue(in LumOnWorldProbeTraceWorkItem item)
    {
        lock (progressGate)
        {
            if (stopped || !work.Writer.TryWrite(item)) return false;
            outstanding++;
        }

        Interlocked.Increment(ref approxQueuedWorkItems);
        return true;
    }

    public bool TryDequeueResult(out LumOnWorldProbeTraceResult result)
    {
        if (!results.Reader.TryRead(out result))
        {
            return false;
        }

        Interlocked.Decrement(ref approxQueuedResults);
        return true;
    }

    public int ApproxQueuedWorkItems => Volatile.Read(ref approxQueuedWorkItems);

    public int ApproxQueuedResults => Volatile.Read(ref approxQueuedResults);

    public void CancelOutstanding()
    {
        // Backpressure/cancellation hook: cancels the worker and ends the session.
        // The caller is expected to recreate the service on large camera teleports.
        cts.Cancel();
        lock (progressGate) { stopped = true; progress?.TrySetResult(); }
    }

    public void Dispose()
    {
        lock (progressGate) { stopped = true; progress?.TrySetResult(); }
        cts.Cancel();
        work.Writer.TryComplete();

        try
        {
            workerTask.Wait(TimeSpan.FromSeconds(1));
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
                while (work.Reader.TryRead(out var item))
                {
                    Interlocked.Decrement(ref approxQueuedWorkItems);

                    // Claim-based scheduling: only transition Queued -> InFlight once the worker actually starts.
                    // If the probe is no longer queued (e.g., disabled/invalidated/replaced), drop the work item.
                    if (!tryClaim(item.Request, item.FrameIndex))
                    {
                        CompleteAdmission();
                        continue;
                    }

                    using var scope = Profiler.BeginScope("LumOn.WorldProbe.Trace.Run", "LumOn");
                    LumOnWorldProbeTraceResult res;
                    try
                    {
                        res = integrator.TraceProbe(scene, item, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Ensure the scheduler can recover by retrying later.
                        res = new LumOnWorldProbeTraceResult(
                            FrameIndex: item.FrameIndex,
                            Request: item.Request,
                            Success: false,
                            FailureReason: WorldProbeTraceFailureReason.Exception,
                            AtlasSamples: ImmutableArray<LumOnWorldProbeAtlasSample>.Empty,
                            SkyIntensity: 0f,
                            ShortRangeAoDirWorld: default,
                            ShortRangeAoConfidence: 0f,
                            Confidence: 0f,
                            MeanLogHitDistance: 0f,
                            ImportanceFlags: LumOnWorldProbeImportanceFlags.None);
                    }

                    await results.Writer.WriteAsync(res, cts.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref approxQueuedResults);
                    CompleteAdmission();
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
        finally
        {
            results.Writer.TryComplete();
            lock (progressGate) { stopped = true; progress?.TrySetResult(); }
        }
    }
}
