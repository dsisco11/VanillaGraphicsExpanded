using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Preserves native standard-material deformation controls alongside shared virtual resources.</summary>
internal interface IShadowStaticBindings : IShadowTerrainBindings
{
    #region Public API
    /// <summary>Supplies renderer-added flags and the native no/full/quarter-warp policy.</summary>
    [ShaderBinding("VgeShadowObjectFlags", ShaderBindingKind.UniformBlock, 1, ShaderStageKind.Vertex)]
    CpuUniformBuffer ObjectFlags { get; }
    #endregion
}
