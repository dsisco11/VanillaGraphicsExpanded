using System;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>Routes admissions by level while sharing scheduling and publication budgets.</summary>
internal sealed class LumOnWorldProbeTraceRouter : IWorldProbeTraceBackend
{
    private readonly IWorldProbeTraceBackend cpu;
    private readonly IWorldProbeTraceBackend gpu;
    private bool pollGpuFirst;
    public bool EnableGpuTracing { get; }

    #region Lifecycle
    /// <summary>Owns both backends for one immutable routing lifetime.</summary>
    public LumOnWorldProbeTraceRouter(bool enableGpuTracing, IWorldProbeTraceBackend cpu, IWorldProbeTraceBackend gpu)
    {
        EnableGpuTracing = enableGpuTracing;
        this.cpu = cpu ?? throw new ArgumentNullException(nameof(cpu));
        this.gpu = gpu ?? throw new ArgumentNullException(nameof(gpu));
    }

    /// <summary>Retires both backends before a replacement routing lifetime can publish.</summary>
    public void Dispose()
    {
        gpu.Dispose();
        cpu.Dispose();
    }
    #endregion

    #region Admission and completion
    /// <summary>Shares frame boundaries with both backends without granting extra credit per result poll.</summary>
    public void BeginFrame(int frameIndex)
    {
        cpu.BeginFrame(frameIndex); gpu.BeginFrame(frameIndex);
    }

    /// <summary>Routes only L0 to the optional GPU backend.</summary>
    public bool TryEnqueue(in LumOnWorldProbeTraceWorkItem item) =>
        (EnableGpuTracing && item.Request.Level == 0 ? gpu : cpu).TryEnqueue(item);

    /// <summary>Alternates completion priority so either backend can progress under a shared limit.</summary>
    public bool TryDequeueResult(out LumOnWorldProbeTraceResult result)
    {
        pollGpuFirst = !pollGpuFirst;
        var first = pollGpuFirst ? gpu : cpu;
        var second = pollGpuFirst ? cpu : gpu;
        return first.TryDequeueResult(out result) || second.TryDequeueResult(out result);
    }
    #endregion
}
