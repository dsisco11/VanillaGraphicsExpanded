using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains one backend execution exclusion independently of canonical metadata transactions.</summary>
internal interface IRHIResourceOperation : IDisposable
{
    /// <summary>Gets the original resource identity protected by this exclusion.</summary>
    GpuResourceHandle Handle { get; }
    /// <summary>Reports interruption without authorizing metadata publication.</summary>
    bool Cancelled { get; }
    /// <summary>Reports native deletion of the original identity.</summary>
    bool Deleted { get; }
    /// <summary>Prevents a caught participating failure from certifying the enclosing result.</summary>
    void Interrupt();
}
