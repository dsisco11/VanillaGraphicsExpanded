using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Profiling;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal sealed class LumOnWorldProbeTraceService : IDisposable
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
        if (!work.Writer.TryWrite(item))
        {
            return false;
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
    }

    public void Dispose()
    {
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
                            ShR: default,
                            ShG: default,
                            ShB: default,
                            ShSky: default,
                            SkyIntensity: 0f,
                            ShortRangeAoDirWorld: default,
                            ShortRangeAoConfidence: 0f,
                            Confidence: 0f,
                            MeanLogHitDistance: 0f);
                    }

                    await results.Writer.WriteAsync(res, cts.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref approxQueuedResults);
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
        }
    }
}
