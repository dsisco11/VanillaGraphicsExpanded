using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>L0 backend entry point, using the shared CPU queue until compute tracing is implemented.</summary>
internal sealed class LumOnWorldProbeGpuTraceBackend : IWorldProbeTraceBackend
{
    private readonly IWorldProbeTraceBackend fallback;

    #region Lifecycle
    /// <summary>Borrows the CPU backend to keep L0 functional during incremental implementation.</summary>
    public LumOnWorldProbeGpuTraceBackend(IWorldProbeTraceBackend fallback) => this.fallback = fallback;

    /// <summary>The router owns the borrowed CPU backend; no GPU resources exist yet.</summary>
    public void Dispose() { }
    #endregion

    #region Admission and completion
    /// <summary>Preserves the original admission and work budget through the temporary CPU path.</summary>
    public bool TryEnqueue(in LumOnWorldProbeTraceWorkItem item) => fallback.TryEnqueue(item);

    /// <summary>Temporary CPU completions are drained once through the router's CPU backend.</summary>
    public bool TryDequeueResult(out LumOnWorldProbeTraceResult result)
    {
        result = default;
        return false;
    }
    #endregion
}
