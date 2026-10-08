using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Declares the preserved SSAO receiver and kernel resources.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface IPostSsaoShaderProgramBindings {
    #region Public API
    /// <summary>Supplies the projection and sample kernel.</summary>
    [ShaderBinding("SsaoInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Samples the gNormal receiver data.</summary>
    [ShaderBinding("gNormal", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Normal { set; }
    /// <summary>Samples the gPosition receiver data.</summary>
    [ShaderBinding("gPosition", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Position { set; }
    /// <summary>Samples the revealage receiver data.</summary>
    [ShaderBinding("revealage", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Revealage { set; }
    #endregion
}
