using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnHzbDownsampleShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumOnHzbDownsampleShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the VgeLumOnHzbDownsampleParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnHzbDownsampleParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the hzbDepth Sampler slot.</summary>
    [ShaderBinding("hzbDepth", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuTexture? HzbDepth { set; }
    #endregion
}
