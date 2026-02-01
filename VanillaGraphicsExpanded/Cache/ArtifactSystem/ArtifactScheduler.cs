using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

internal sealed class ArtifactScheduler<TKey, TOutput> : IArtifactScheduler<TKey>
    where TKey : notnull
{
    private readonly ICoreClientAPI capi;
    private readonly IArtifactComputer<TKey, TOutput> computer;
    private readonly IArtifactOutputStage<TKey, TOutput>? outputStage;
    private readonly IArtifactApplier<TKey, TOutput>? applier;

    private readonly ArtifactPriorityFifoQueue<TKey> queue = new();

    private readonly IArtifactReservationPool? diskReservations;
    private readonly IArtifactReservationPool? gpuReservations;

    private readonly object lifecycleGate = new();
    private CancellationTokenSource? runCts;
    private CancellationTokenSource? sessionCts;
    private Task? runLoopTask;

    private readonly SemaphoreSlim concurrencyGate;
    private readonly int maxConcurrency;

    private long sessionId;

    private long completed;
    private long errors;
    private long inFlight;

    private long computeTicks;
    private long outputTicks;
    private long computeSamples;
    private long outputSamples;

    private long pendingApply;
    private readonly object idleGate = new();
    private TaskCompletionSource<object?> idleTcs = CreateCompletedIdleTcs();

    public ArtifactScheduler(
        ICoreClientAPI capi,
        IArtifactComputer<TKey, TOutput> computer,
        IArtifactOutputStage<TKey, TOutput>? outputStage,
        IArtifactApplier<TKey, TOutput>? applier,
        int maxConcurrency = 1,
        IArtifactReservationPool? diskReservations = null,
        IArtifactReservationPool? gpuReservations = null)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.computer = computer ?? throw new ArgumentNullException(nameof(computer));
        this.outputStage = outputStage;
        this.applier = applier;
        this.diskReservations = diskReservations;
        this.gpuReservations = gpuReservations;

        this.maxConcurrency = Math.Clamp(maxConcurrency, 1, 32);
        concurrencyGate = new SemaphoreSlim(this.maxConcurrency, this.maxConcurrency);
    }

    public void Start()
    {
        lock (lifecycleGate)
        {
            if (runCts is not null)
            {
                return;
            }

            runCts = new CancellationTokenSource();
            ResetSession_NoLock();
            runLoopTask = Task.Run(() => RunLoopAsync(runCts.Token), runCts.Token);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? stopRun;
        CancellationTokenSource? stopSession;
        Task? loop;

        lock (lifecycleGate)
        {
            stopRun = runCts;
            stopSession = sessionCts;
            loop = runLoopTask;

            runCts = null;
            sessionCts = null;
            runLoopTask = null;
        }

        if (stopSession is not null)
        {
            try { stopSession.Cancel(); } catch { }
            try { stopSession.Dispose(); } catch { }
        }

        if (stopRun is not null)
        {
            try { stopRun.Cancel(); } catch { }
            try { stopRun.Dispose(); } catch { }
        }

        // Best-effort: do not block the game thread.
        _ = loop;

        // After stop, we treat the scheduler as quiescent for waiters even if the internal queue still has entries.
        // (Nothing will drain them once the run loop is canceled.)
        ForceSignalIdle();
    }

    public void BumpSession()
    {
        lock (lifecycleGate)
        {
            if (runCts is null)
            {
                sessionId = unchecked(sessionId + 1);
                return;
            }

            ResetSession_NoLock();
        }
    }

    public void FinishOnCurrentThread(CancellationToken cancellationToken = default)
    {
        lock (lifecycleGate)
        {
            if (runCts is not null)
            {
                throw new InvalidOperationException("FinishOnCurrentThread requires the scheduler to not be started. Call this before Start(), or stop and recreate the scheduler for a synchronous loading-screen drain.");
            }
        }

        long capturedSessionId = Interlocked.Read(ref sessionId);
        CancellationToken sessionToken = sessionCts?.Token ?? CancellationToken.None;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken);
        CancellationToken token = linkedCts.Token;

        while (!token.IsCancellationRequested && queue.TryDequeue(out IArtifactWorkItem<TKey> item))
        {
            ArtifactSession session = new(capturedSessionId, token, ArtifactExecutionMode.InlineCurrentThread);
            var ctx = new ArtifactComputeContext<TKey>(capi, session, item.Key);

            try
            {
                ArtifactComputeResult<TOutput> result = computer.ComputeAsync(ctx).GetAwaiter().GetResult();

                if (token.IsCancellationRequested || capturedSessionId != Interlocked.Read(ref sessionId))
                {
                    continue;
                }

                if (!result.IsNoop && result.Output.HasValue && outputStage is not null)
                {
                    outputStage.OutputAsync(new ArtifactOutputContext<TKey, TOutput>(capi, session, item.Key, result.Output.Value))
                        .GetAwaiter().GetResult();
                }

                if (result.RequiresApply && applier is not null)
                {
                    var applyCtx = new ArtifactApplyContext<TKey, TOutput>(capi, session, item.Key, result.Output);
                    applier.Apply(in applyCtx);
                }

                Interlocked.Increment(ref completed);
            }
            catch
            {
                Interlocked.Increment(ref errors);
            }
        }

        SignalIdleIfNeeded();
    }

    public bool Enqueue(IArtifactWorkItem<TKey> item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        bool enqueued = queue.TryEnqueue(item.Priority, item);
        if (enqueued)
        {
            MarkNonIdle();
        }
        return enqueued;
    }

    public async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsIdle() || !IsRunning())
            {
                return;
            }

            Task task;
            lock (idleGate)
            {
                // Defensive: there is a very small race where the queue transitions to non-idle
                // before Enqueue() updates idleTcs. If that happens, self-heal here to avoid a spin.
                if (!IsIdle() && idleTcs.Task.IsCompleted)
                {
                    idleTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                task = idleTcs.Task;
            }

            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ArtifactSchedulerStats GetStatsSnapshot()
    {
        long queued = queue.Count;
        long inflight = Interlocked.Read(ref inFlight);
        long completedCount = Interlocked.Read(ref completed);
        long errorCount = Interlocked.Read(ref errors);

        double tickToMs = 1000.0 / Stopwatch.Frequency;
        long cSamples = Interlocked.Read(ref computeSamples);
        long oSamples = Interlocked.Read(ref outputSamples);

        double avgComputeMs = cSamples > 0 ? (Interlocked.Read(ref computeTicks) * tickToMs) / cSamples : 0.0;
        double avgOutputMs = oSamples > 0 ? (Interlocked.Read(ref outputTicks) * tickToMs) / oSamples : 0.0;

        return new ArtifactSchedulerStats(
            Queued: queued,
            InFlight: inflight,
            Completed: completedCount,
            Errors: errorCount,
            AvgComputeMs: avgComputeMs,
            AvgOutputMs: avgOutputMs);
    }

    private void ResetSession_NoLock()
    {
        sessionId = unchecked(sessionId + 1);

        CancellationTokenSource? old = sessionCts;
        sessionCts = new CancellationTokenSource();

        if (old is not null)
        {
            try { old.Cancel(); } catch { }
            try { old.Dispose(); } catch { }
        }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!queue.TryDequeue(out IArtifactWorkItem<TKey> item))
            {
                SignalIdleIfNeeded();
                try { await Task.Delay(5, token).ConfigureAwait(false); } catch { }
                continue;
            }

            // Admission/backpressure (Option B): never block; defer when capacity unavailable.
            if (!TryAcquireReservations(item, out ArtifactReservationToken diskToken, out ArtifactReservationToken gpuToken))
            {
                // Requeue at the same priority and yield briefly.
                queue.TryEnqueue(item.Priority, item);
                try { await Task.Delay(1, token).ConfigureAwait(false); } catch { }
                continue;
            }

            await concurrencyGate.WaitAsync(token).ConfigureAwait(false);

            long capturedSessionId = Interlocked.Read(ref sessionId);
            CancellationToken sessionToken = sessionCts?.Token ?? CancellationToken.None;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, sessionToken);
            CancellationToken workToken = linkedCts.Token;

            Interlocked.Increment(ref inFlight);

            _ = Task.Run(async () =>
            {
                try
                {
                    using (diskToken)
                    using (gpuToken)
                    {
                    ArtifactSession session = new(capturedSessionId, workToken, ArtifactExecutionMode.Background);
                    var ctx = new ArtifactComputeContext<TKey>(capi, session, item.Key);

                    long c0 = Stopwatch.GetTimestamp();
                    ArtifactComputeResult<TOutput> result = await computer.ComputeAsync(ctx).ConfigureAwait(false);
                    long c1 = Stopwatch.GetTimestamp();
                    Interlocked.Add(ref computeTicks, c1 - c0);
                    Interlocked.Increment(ref computeSamples);

                    if (workToken.IsCancellationRequested || capturedSessionId != Interlocked.Read(ref sessionId))
                    {
                        return;
                    }

                    if (!result.IsNoop && result.Output.HasValue && outputStage is not null)
                    {
                        long o0 = Stopwatch.GetTimestamp();
                        await outputStage.OutputAsync(new ArtifactOutputContext<TKey, TOutput>(capi, session, item.Key, result.Output.Value)).ConfigureAwait(false);
                        long o1 = Stopwatch.GetTimestamp();
                        Interlocked.Add(ref outputTicks, o1 - o0);
                        Interlocked.Increment(ref outputSamples);
                    }

                    if (result.RequiresApply && applier is not null)
                    {
                        Interlocked.Increment(ref pendingApply);

                        try
                        {
                            capi.Event.EnqueueMainThreadTask(() =>
                            {
                                try
                                {
                                    if (capturedSessionId != Interlocked.Read(ref sessionId))
                                    {
                                        return;
                                    }

                                    var applyCtx = new ArtifactApplyContext<TKey, TOutput>(capi, session, item.Key, result.Output);
                                    applier.Apply(in applyCtx);
                                }
                                finally
                                {
                                    Interlocked.Decrement(ref pendingApply);
                                    SignalIdleIfNeeded();
                                }
                            }, "vge-artifact-apply");
                        }
                        catch
                        {
                            Interlocked.Decrement(ref pendingApply);
                            SignalIdleIfNeeded();
                        }
                    }

                    Interlocked.Increment(ref completed);
                    }
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                }
                finally
                {
                    concurrencyGate.Release();
                    Interlocked.Decrement(ref inFlight);
                    SignalIdleIfNeeded();
                }
            }, CancellationToken.None);

            await Task.Yield();
        }
    }

    private bool IsRunning()
    {
        lock (lifecycleGate)
        {
            return runCts is not null;
        }
    }

    private bool IsIdle()
        => queue.Count == 0
            && Interlocked.Read(ref inFlight) == 0
            && Interlocked.Read(ref pendingApply) == 0;

    private void MarkNonIdle()
    {
        lock (idleGate)
        {
            if (idleTcs.Task.IsCompleted)
            {
                idleTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private void SignalIdleIfNeeded()
    {
        if (!IsIdle())
        {
            return;
        }

        lock (idleGate)
        {
            if (!IsIdle())
            {
                return;
            }

            idleTcs.TrySetResult(null);
        }
    }

    private void ForceSignalIdle()
    {
        lock (idleGate)
        {
            idleTcs.TrySetResult(null);
        }
    }

    private static TaskCompletionSource<object?> CreateCompletedIdleTcs()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.TrySetResult(null);
        return tcs;
    }

    private bool TryAcquireReservations(
        IArtifactWorkItem<TKey> item,
        out ArtifactReservationToken diskToken,
        out ArtifactReservationToken gpuToken)
    {
        diskToken = default;
        gpuToken = default;

        bool wantsDisk = (item.RequiredOutputKinds & ArtifactOutputKinds.Disk) != 0;
        bool wantsGpu = (item.RequiredOutputKinds & ArtifactOutputKinds.Gpu) != 0;

        if (wantsDisk && diskReservations is not null)
        {
            if (!diskReservations.TryAcquire(out diskToken))
            {
                return false;
            }
        }

        if (wantsGpu && gpuReservations is not null)
        {
            if (!gpuReservations.TryAcquire(out gpuToken))
            {
                diskToken.Dispose();
                diskToken = default;
                return false;
            }
        }

        return true;
    }

    private sealed class ArtifactPriorityFifoQueue<T>
        where T : notnull
    {
        private readonly object gate = new();
        private readonly SortedDictionary<int, Queue<IArtifactWorkItem<T>>> queuesByPriority = new();
        private readonly HashSet<T> queuedKeys = new();

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return queuedKeys.Count;
                }
            }
        }

        public bool TryEnqueue(int priority, IArtifactWorkItem<T> item)
        {
            lock (gate)
            {
                if (!queuedKeys.Add(item.Key))
                {
                    return false;
                }

                if (!queuesByPriority.TryGetValue(priority, out Queue<IArtifactWorkItem<T>>? q))
                {
                    q = new Queue<IArtifactWorkItem<T>>();
                    queuesByPriority[priority] = q;
                }

                q.Enqueue(item);
                return true;
            }
        }

        public bool TryDequeue(out IArtifactWorkItem<T> item)
        {
            lock (gate)
            {
                if (queuesByPriority.Count == 0)
                {
                    item = default!;
                    return false;
                }

                int highestPriority = int.MinValue;
                Queue<IArtifactWorkItem<T>>? highestQueue = null;

                foreach ((int priority, Queue<IArtifactWorkItem<T>> q) in queuesByPriority)
                {
                    highestPriority = priority;
                    highestQueue = q;
                }

                if (highestQueue is null || highestQueue.Count == 0)
                {
                    item = default!;
                    return false;
                }

                item = highestQueue.Dequeue();
                queuedKeys.Remove(item.Key);

                if (highestQueue.Count == 0)
                {
                    queuesByPriority.Remove(highestPriority);
                }

                return true;
            }
        }
    }
}
