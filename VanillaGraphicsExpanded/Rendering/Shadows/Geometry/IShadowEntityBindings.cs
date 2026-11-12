using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Adds native animation and object flags to the shared virtual material resource contract.</summary>
internal interface IShadowEntityBindings : IShadowTerrainBindings
{
    #region Public API
    /// <summary>Preserves native joint indexing within the installed game's bounded animation palette.</summary>
    [ShaderBinding("VgeShadowAnimation", ShaderBindingKind.UniformBlock, 1, ShaderStageKind.Vertex)]
    CpuUniformBuffer Animation { get; }
    /// <summary>Retains native renderer-added deformation flags.</summary>
    [ShaderBinding("VgeShadowEntityFlags", ShaderBindingKind.UniformBlock, 2, ShaderStageKind.Vertex)]
    CpuUniformBuffer Flags { get; }
    #endregion
}
