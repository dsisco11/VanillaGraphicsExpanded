using System;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Owns verified geometry; only concrete upload adapters can issue its native draw.</summary>
internal abstract class GraphicsGeometry : IDisposable
{
    #region Public API
    /// <summary>Validates lifetime, layout, topology and ranges without querying the driver.</summary>
    internal abstract void Validate(GraphicsPipelineDesc pipeline, GraphicsDraw draw);
    /// <summary>Emits a previously validated draw through the adapter's declared binding operations.</summary>
    internal abstract void Submit(GraphicsDraw draw);
    /// <summary>Retires adapter-owned geometry storage.</summary>
    public abstract void Dispose();
    #endregion
}
