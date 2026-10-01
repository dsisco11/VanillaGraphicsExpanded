using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares the GPU binding contract for LumOnVelocityShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumOnVelocityShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer? LumOnFrame { get; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int PrimaryDepth { set; }
    /// <summary>Declares the gBufferPatchId Sampler slot.</summary>
    [ShaderBinding("gBufferPatchId", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int PatchIdentity { set; }
    /// <summary>Supplies optional shared world-probe storage.</summary>
    [ShaderBinding("LumOnWorldProbeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.WorldProbe, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    GpuUniformBuffer? LumOnWorldProbe { get; }
    #endregion
}
