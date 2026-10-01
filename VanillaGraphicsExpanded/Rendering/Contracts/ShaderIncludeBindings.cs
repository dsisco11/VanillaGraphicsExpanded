using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Owns a shared compile-time GPU interface.</summary>
internal static partial class ShaderIncludeBindings
{
    #region Private
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderUniformBlockBinding LumOnFrame { get; }
    /// <summary>Declares the LumOnWorldProbeUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderUniformBlockBinding LumOnWorldProbe { get; }
    /// <summary>Declares the LumOnNearFieldUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnNearFieldUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Material, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderUniformBlockBinding LumOnNearField { get; }
    /// <summary>Declares the LumOnTerrainBridgeUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnTerrainBridgeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.TerrainBridge, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderUniformBlockBinding LumOnTerrainBridge { get; }
    #endregion
}
