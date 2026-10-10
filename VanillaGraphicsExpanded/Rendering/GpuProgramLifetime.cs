using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Stores one program owner's CPU lifetime and publication guards.</summary>
internal sealed class GpuProgramLifetime
{
    #region Internal API
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
        ObjectDisposedException.ThrowIf(IsRetired, this);
        RequireOutsidePublication();
    }
    #endregion
}
