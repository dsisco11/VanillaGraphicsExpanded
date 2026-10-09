using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Declares resources consumed by pbr_ao_depth.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface IAmbientOcclusionDepthShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies the coherent draw parameter block.</summary>
    [ShaderBinding("PostprocessInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies sourceImage through the retained typed texture contract.</summary>
    [ShaderBinding("sourceImage", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? SourceImage { set; }
    #endregion
}
