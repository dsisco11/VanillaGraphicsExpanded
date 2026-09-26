using System;
using System.Numerics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Profiling;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Retraces only GPU coverage/unsupported directions within bounded storage and per-frame CPU ray credit.</summary>
internal sealed class WorldProbeCpuFallbackService : IDisposable
{
    public const int MaximumAdmissions = 64;
    public const int MaximumRetainedRays = 8192;
    public const int RaysPerFrame = 256;
    private readonly object gate = new();
    private readonly IWorldProbeTraceScene scene;
    private readonly Func<LumOnWorldProbeUpdateRequest, bool> isCurrent;
    private readonly Channel<WorldProbeCpuFallbackWork> work = Channel.CreateBounded<WorldProbeCpuFallbackWork>(MaximumAdmissions);
    private readonly Channel<WorldProbeCpuFallbackResult> results = Channel.CreateBounded<WorldProbeCpuFallbackResult>(MaximumAdmissions);
    private readonly CancellationTokenSource cancellation = new();
    private readonly SemaphoreSlim creditAvailable = new(0, 1);
    private readonly Task worker;
    private int admissions, retainedRays, credit, lastFrame;
    private bool hasFrame, disposed, waitingForCredit, workerCompleted;
    private TaskCompletionSource? progress;

    #region Lifecycle and budgets
    /// <summary>Starts one collision-only worker; neither callback nor scene may access GPU resources.</summary>
    public WorldProbeCpuFallbackService(IWorldProbeTraceScene scene, Func<LumOnWorldProbeUpdateRequest, bool> isCurrent)
    {
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
        this.isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        worker = Task.Run(Run);
    }

    /// <summary>Replenishes at most 256 ray starts once per rendered frame; unused credit never accumulates.</summary>
    public void BeginFrame(int frame)
    {
        lock (gate)
        {
            if (disposed || (hasFrame && frame == lastFrame)) return;
            hasFrame = true; lastFrame = frame; credit = RaysPerFrame; waitingForCredit = false;
            if (creditAvailable.CurrentCount == 0) creditAvailable.Release();
        }
    }

    /// <summary>Cancels without waiting on terrain access; synchronization resources retire after the worker exits.</summary>
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; SignalProgress(); }
        cancellation.Cancel(); work.Writer.TryComplete();
        _ = worker.ContinueWith(_ => { cancellation.Dispose(); creditAvailable.Dispose(); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    #endregion

    #region Admission and completion
    /// <summary>Reports retained work that has not yet produced an available completion.</summary>
    internal bool HasOutstandingWork { get { lock (gate) return !disposed && !workerCompleted && admissions > 0 && !results.Reader.TryPeek(out _); } }

    /// <summary>Reports whether another frame's ray allowance is needed before the worker can continue.</summary>
    internal bool RequiresFrameCredit { get { lock (gate) return waitingForCredit; } }

    /// <summary>Observes completion, idle, exhausted frame allowance, or shutdown without consuming work.</summary>
    internal Task WaitForProgressAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            // Atomic check/subscription avoids missing completion that precedes observer registration.
            if (disposed || workerCompleted || admissions == 0 || waitingForCredit || results.Reader.TryPeek(out _))
                return Task.CompletedTask;
            return (progress ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Wakes observers under the admission gate and prepares the next progress notification.</summary>
    private void SignalProgress()
    {
        var previous = progress;
        progress = null;
        previous?.TrySetResult();
    }

    /// <summary>Allows the single producer to avoid copying retained answers while the outstanding-work budget is full.</summary>
    public bool HasCapacity(int rayCount)
    {
        lock (gate) return !disposed && rayCount > 0 && admissions < MaximumAdmissions &&
            rayCount <= MaximumRetainedRays - retainedRays;
    }

    /// <summary>Charges queued, running and completed-but-undrained work against the same storage bounds.</summary>
    public bool TryEnqueue(in WorldProbeCpuFallbackWork item)
    {
        if (item.Answers.IsDefaultOrEmpty || item.Directions.IsDefault || item.Directions.Length != item.Answers.Length)
            throw new ArgumentException("Fallback directions must match retained answers.", nameof(item));
        lock (gate)
        {
            if (disposed || admissions >= MaximumAdmissions || item.Answers.Length > MaximumRetainedRays - retainedRays) return false;
            admissions++; retainedRays += item.Answers.Length;
            if (work.Writer.TryWrite(item)) return true;
            admissions--; retainedRays -= item.Answers.Length;
            return false;
        }
    }

    /// <summary>Releases storage credit only when ownership of a completion returns to the render thread.</summary>
    public bool TryDequeue(out WorldProbeCpuFallbackResult result)
    {
        if (!results.Reader.TryRead(out result)) return false;
        lock (gate) { admissions--; retainedRays -= result.Work.Answers.Length; }
        return true;
    }
    #endregion

    #region Worker
    /// <summary>Waits for frame credit without polling or blocking the render thread.</summary>
    private async Task TakeCredit(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (credit > 0) { credit--; return; }
                waitingForCredit = true;
                SignalProgress();
            }
            await creditAvailable.WaitAsync(token).ConfigureAwait(false);
        }
    }

    /// <summary>Preserves all non-fallback answers and retries only the original full segments requiring CPU geometry.</summary>
    private async Task Run()
    {
        var token = cancellation.Token;
        try
        {
            await foreach (var item in work.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                var completed = item;
                bool success = false;
                try
                {
                    var merged = item.Answers.ToBuilder();
                    success = isCurrent(item.Item.Request);
                    for (int index = 0; success && index < merged.Count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!merged[index].RequiresCpuFallback) continue;
                        await TakeCredit(token).ConfigureAwait(false);
                        if (!isCurrent(item.Item.Request)) { success = false; break; }
                        merged[index] = Trace(item.Item, item.Directions[index], token);
                    }
                    success &= isCurrent(item.Item.Request);
                    if (success) completed = item with { Answers = merged.ToImmutable() };
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { success = false; }
                await results.Writer.WriteAsync(new(completed, success), token).ConfigureAwait(false);
                lock (gate) SignalProgress();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { results.Writer.TryComplete(); lock (gate) { workerCompleted = true; SignalProgress(); } }
    }

    /// <summary>Converts a CPU collision result to the common answer format without sampling block lighting.</summary>
    private WorldProbeTraceAnswerGpu Trace(in LumOnWorldProbeTraceWorkItem item, uint selector, CancellationToken token)
    {
        using var scope = Profiler.BeginScope("LumOn.WorldProbe.Trace.CpuFallback", "LumOn");
        bool nearby = (selector & 0x80000000u) != 0;
        Vector3 direction = nearby ? WorldProbeTraceDirectionSelection.CardinalDirection((int)(selector & 0x7fffffffu))
            : LumOnWorldProbeAtlasDirections.GetDirections(item.WorldProbeOctahedralTileSize)[checked((int)selector)];
        double distance = nearby ? item.NearbySolidHitDistance : item.MaxTraceDistanceWorld;
        var outcome = scene.Trace(item.ProbePosWorld, direction, distance, token, out var hit);
        var answer = new WorldProbeTraceAnswerGpu
        {
            Outcome = outcome switch
            {
                WorldProbeTraceOutcome.Hit => 1, WorldProbeTraceOutcome.Sky => 4,
                WorldProbeTraceOutcome.DistanceLimit => 2, WorldProbeTraceOutcome.BudgetExhausted => 3,
                WorldProbeTraceOutcome.Unavailable => 0, _ => 5,
            },
            Reason = 2, // CPU unavailability is unresolved; it is not another GPU coverage exit.
        };
        if (outcome != WorldProbeTraceOutcome.Hit) return answer;
        Vector3d point = item.ProbePosWorld + Vector3d.Normalize(Vector3d.FromVector3(direction)) * hit.HitDistance;
        Vector3 fraction = new((float)(point.X-hit.HitBlockPos.X), (float)(point.Y-hit.HitBlockPos.Y), (float)(point.Z-hit.HitBlockPos.Z));
        answer.Hit = new SurfaceLightingQuery(hit.HitBlockPos, hit.HitFaceNormal, fraction, hit.HitBlockId);
        answer.Hit.Fraction.W = (float)hit.HitDistance;
        return answer;
    }
    #endregion
}
