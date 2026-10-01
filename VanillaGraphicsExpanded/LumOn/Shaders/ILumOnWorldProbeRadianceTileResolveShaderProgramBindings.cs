using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnWorldProbeRadianceTileResolveShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumOnWorldProbeRadianceTileResolveShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the VgeLumOnWorldProbeResolveParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnWorldProbeResolveParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    #endregion
}
