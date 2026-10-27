using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Stores one program owner's CPU lifetime and publication guards.</summary>
internal sealed class GpuProgramLifetime
{
    #region Internal API
    /// <summary>Supplies the owning registry or client admission without duplicating scope ownership.</summary>
    internal Func<bool>? SubmissionAdmission { get; set; }
    /// <summary>Reports whether native preparation, scoped use and replay remain admitted.</summary>
    internal bool IsSubmissionAdmitted => SubmissionAdmission?.Invoke() != false;
    /// <summary>Rejects closed or reloading client work before native scope capture or publication.</summary>
    internal void RequireSubmissionAdmission()
    {
        if (!IsSubmissionAdmitted) throw new InvalidOperationException("GPU program submission admission is closed.");
    }
    /// <summary>Reuses the native resource borrow contract when the family already has a resource owner.</summary>
    internal GpuResource? NativeExecutable { get; set; }
    /// <summary>Rejects retirement or replacement while scopes retain executable ownership.</summary>
    internal void RequireUnborrowed()
    {
        if (NativeExecutable?.UsageReferences.Pass > 0)
            throw new InvalidOperationException("GPU program generation is borrowed by an active use lifetime.");
    }
    /// <summary>Reports terminal owner retirement independently of executable invalidation.</summary>
    internal bool IsRetired { get; set; }
    /// <summary>Reports an active retained-input publication.</summary>
    internal bool IsPublishing { get; set; }
    /// <summary>Holds CPU blocks explicitly adopted by this owner.</summary>
    internal List<CpuUniformBuffer> OwnedUniforms { get; } = new();
    /// <summary>Rejects executable work from inside retained-input publication.</summary>
    internal void RequireOutsidePublication()
    {
        if (IsPublishing) throw new InvalidOperationException("Cannot activate a GPU program during input publication.");
    }
    /// <summary>Rejects retained-input mutation and retirement while publication is active.</summary>
    internal void RequireMutation()
    {
        RequireLive();
        RequireOutsidePublication();
    }
    /// <summary>Checks terminal logical retirement without preventing read-only capture during its owner guard.</summary>
    internal void RequireLive() => ObjectDisposedException.ThrowIf(IsRetired, this);
    #endregion
}
