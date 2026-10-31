using System;
using System.Diagnostics;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Diagnoses owner misuse without supplying resource ownership or repairing lifecycle state.</summary>
public abstract partial class GpuResource
{
    #region Internal API
    /// <summary>Checks owner affinity at explicit mutation and transfer boundaries in development builds.</summary>
    [Conditional("DEBUG")]
    internal void ValidateOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("Schedule resource mutation on its owning thread.");
    }
    #endregion
}
