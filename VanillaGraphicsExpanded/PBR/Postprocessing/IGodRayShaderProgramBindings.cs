using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Declares the owned pbr_godrays resource contract.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface IGodRayShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies coherent draw parameters.</summary>
    [ShaderBinding("PostprocessInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies visibilityImage with explicit edge-clamped sampling.</summary>
    [ShaderBinding("visibilityImage", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? VisibilityImage { set; }
    /// <summary>Supplies depthImage with explicit edge-clamped sampling.</summary>
    [ShaderBinding("depthImage", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? DepthImage { set; }
    #endregion
}
