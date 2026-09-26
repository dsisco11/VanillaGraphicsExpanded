using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes one pending lighting dependency without taking collection or publication ownership.</summary>
internal static class RuntimeLightingCompletionWait
{
    #region Dependency observation
    /// <summary>Waits only for external progress; frame-budget and deliberately held work remain pump-driven.</summary>
    internal static void Wait(LumOnWorldProbeUpdateRenderer renderer, RuntimeProbeWorld world, TimeSpan timeout)
    {
        var router = Field(renderer, "traceService");
        var gpu = router is null ? null : Field(router, "gpu");
        var queries = Field(renderer, "surfaceQueries");
        var queue = queries is null ? null : Field(queries, "queue");
        var batch = gpu is null ? null : Field(gpu, "batch");
        // A signaled but uncollected fence requires another production frame, not a CPU wait.
        var fence = (queue is null ? null : Field(queue, "fence")) as GpuFence
            ?? (batch is null ? null : Field(batch, "fence")) as GpuFence;
        if (fence is not null)
        {
            var status = fence.Wait(timeout);
            if (status == WaitSyncStatus.WaitFailed) throw new InvalidOperationException("Lighting GPU completion wait failed.");
            if (status == WaitSyncStatus.TimeoutExpired) throw new TimeoutException("Lighting GPU completion exceeded the external-wait budget.");
            return;
        }

        var fallback = gpu is null ? null : Field(gpu, "fallback") as WorldProbeCpuFallbackService;
        var cpu = router is null ? null : Field(router, "cpu") as LumOnWorldProbeTraceService;
        bool fallbackPending = fallback?.HasOutstandingWork == true;
        bool cpuPending = cpu?.HasOutstandingWork == true;
        if (!fallbackPending && !cpuPending) return;
        if (fallbackPending && fallback!.RequiresFrameCredit) return;
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            // Wait for one backend to progress, not every outstanding task. Never move GL work to a continuation.
            var first = fallbackPending ? fallback!.WaitForProgressAsync(cancellation.Token) : cpu!.WaitForProgressAsync(cancellation.Token);
            if (fallbackPending && cpuPending)
                first = Task.WhenAny(first, cpu!.WaitForProgressAsync(cancellation.Token)).Unwrap();
            // A stale claim may finish without entering the gate. Either milestone must release the pump.
            if (world.WorkerHeld)
                first = Task.WhenAny(first, world.WaitForWorkerEntryAsync(cancellation.Token)).Unwrap();
            first.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Lighting CPU completion exceeded the external-wait budget (cpu={cpuPending}, fallback={fallbackPending}).");
        }
        finally { cancellation.Cancel(); }
    }

    /// <summary>Reads an owning runtime field at the test boundary and fails visibly if its layout changes.</summary>
    private static object? Field(object owner, string name) =>
        (owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing lighting dependency: {owner.GetType().Name}.{name}"))
        .GetValue(owner);
    #endregion
}
