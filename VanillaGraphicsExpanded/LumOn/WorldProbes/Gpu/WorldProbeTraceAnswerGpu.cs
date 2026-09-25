using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>An 80-byte GPU-produced output record; origins and direction vectors are never repeated in it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WorldProbeTraceAnswerGpu
{
    public int Outcome, Reason, Reserved0, Reserved1;
    public SurfaceLightingQuery Hit;
    public readonly bool RequiresCpuFallback => Outcome == 0 && Reason is 1 or 3;
    public readonly WorldProbeTraceOutcome TraceOutcome => Outcome switch
    {
        1 => WorldProbeTraceOutcome.Hit, 2 => WorldProbeTraceOutcome.DistanceLimit,
        3 => WorldProbeTraceOutcome.BudgetExhausted, 4 => WorldProbeTraceOutcome.Sky,
        _ => WorldProbeTraceOutcome.Unavailable,
    };
}
