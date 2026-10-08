using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Declares typed resources for owned final composition.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface IFinalDisplayShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies the captured display parameters.</summary>
    [ShaderBinding("FinalDisplayInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies the SceneImage texture without transferring ownership.</summary>
    [ShaderBinding("sceneImage", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? SceneImage { set; }
    /// <summary>Supplies the BloomImage texture without transferring ownership.</summary>
    [ShaderBinding("bloomImage", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? BloomImage { set; }
    /// <summary>Supplies the ShaftImage texture without transferring ownership.</summary>
    [ShaderBinding("shaftImage", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? ShaftImage { set; }
    /// <summary>Supplies the OcclusionImage texture without transferring ownership.</summary>
    [ShaderBinding("occlusionImage", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? OcclusionImage { set; }
    /// <summary>Supplies the ExposureImage texture without transferring ownership.</summary>
    [ShaderBinding("exposureImage", ShaderBindingKind.Sampler, 4, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? ExposureImage { set; }
    #endregion
}
