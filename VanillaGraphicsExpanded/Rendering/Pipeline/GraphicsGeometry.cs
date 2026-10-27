using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Supplies immutable geometry inputs while retaining producer-specific storage ownership.</summary>
internal abstract class GraphicsGeometry : IDisposable
{
    #region Public API
    /// <summary>Gets a bounded immutable packet; validation and native rendering belong to the RHI backend.</summary>
    internal abstract RHIGeometryInputs Inputs { get; }
    /// <summary>Retires adapter-owned geometry storage.</summary>
    public abstract void Dispose();
    #endregion
}
