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

    private readonly LumOnWorldProbeTraceIntegrator integrator = new();

    public LumOnWorldProbeTraceService(IWorldProbeTraceScene scene, int maxQueuedWorkItems)
    {
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));

        var workOpts = new BoundedChannelOptions(Math.Max(1, maxQueuedWorkItems))
        {
            // IMPORTANT: Never silently drop queued work.
            // Dropping would leave the corresponding probe "InFlight" forever on the scheduler side.
            // Use Wait so TryWrite fails when full (non-blocking backpressure).
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
