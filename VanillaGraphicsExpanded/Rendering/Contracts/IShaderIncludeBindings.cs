using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Declares the GPU binding contract for ShaderIncludeBindings.</summary>
internal interface IShaderIncludeBindings
{
    #region Public API
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.LumOnFrame, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute, Required = false)]
    ShaderUniformBlockBinding LumOnFrame { get; }
    /// <summary>Declares the LumOnWorldProbeUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute, Required = false)]
    ShaderUniformBlockBinding LumOnWorldProbe { get; }
    /// <summary>Declares the LumOnNearFieldUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnNearFieldUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Material, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute, Required = false)]
    ShaderUniformBlockBinding LumOnNearField { get; }
    #endregion
}
