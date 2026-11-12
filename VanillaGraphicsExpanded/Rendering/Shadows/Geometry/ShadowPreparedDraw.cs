using System;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Exposes one prepared native-compatible drawing interval to the complete-scene submission owner.</summary>
internal abstract class ShadowPreparedDraw : IDisposable
{
    #region Public API
    /// <summary>Supplies the exact realization required by the RHI state-restoration boundary.</summary>
    internal abstract RHIGraphicsPipeline Pipeline { get; }
    /// <summary>Identifies the immutable virtual view used by this native group.</summary>
    internal abstract int ViewIndex { get; }
    /// <summary>Identifies every CPU-known command slot submitted by this group.</summary>
    internal abstract ShadowDrawInterval Interval { get; }
    /// <summary>Identifies the exact GPU command owner whose admission certificate covers this draw.</summary>
    internal abstract ShadowGeometrySelection Selection { get; }
    /// <summary>Rechecks every borrowed scene and material dependency before drawing and publication.</summary>
    internal abstract bool IsCurrent { get; }
    /// <summary>Submits initialized indirect commands inside the caller's declared RHI boundary.</summary>
    internal abstract void Submit();
    /// <summary>Releases private input captures without taking ownership of native geometry.</summary>
    public abstract void Dispose();
    #endregion
}
