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

    private readonly CancellationTokenSource cts = new();

    private readonly Task workerTask;

    private readonly IWorldProbeTraceScene scene;

    private readonly LumOnWorldProbeTraceIntegrator integrator = new();

    public LumOnWorldProbeTraceService(IWorldProbeTraceScene scene, int maxQueuedWorkItems)
    {
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));

        var workOpts = new BoundedChannelOptions(Math.Max(1, maxQueuedWorkItems))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        };

        work = Channel.CreateBounded<LumOnWorldProbeTraceWorkItem>(workOpts);
        results = Channel.CreateUnbounded<LumOnWorldProbeTraceResult>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });

        workerTask = Task.Run(WorkerLoop);
    }

    public bool TryEnqueue(in LumOnWorldProbeTraceWorkItem item)
    {
        return work.Writer.TryWrite(item);
    }

    public bool TryDequeueResult(out LumOnWorldProbeTraceResult result)
    {
        return results.Reader.TryRead(out result);
    }

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
                    using var scope = Profiler.BeginScope("LumOn.WorldProbe.Trace.Run", "LumOn");
                    LumOnWorldProbeTraceResult res = integrator.TraceProbe(scene, item, cts.Token);
                    await results.Writer.WriteAsync(res, cts.Token).ConfigureAwait(false);
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
