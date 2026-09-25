using System;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>Accepts shared scheduler admissions and returns results under their original tickets.</summary>
internal interface IWorldProbeTraceBackend : IDisposable
{
    /// <summary>Accepts work within backend capacity without changing its direction selection or budget.</summary>
    bool TryEnqueue(in LumOnWorldProbeTraceWorkItem item);

    /// <summary>Returns a completed admission for the shared publication path.</summary>
    bool TryDequeueResult(out LumOnWorldProbeTraceResult result);
}
