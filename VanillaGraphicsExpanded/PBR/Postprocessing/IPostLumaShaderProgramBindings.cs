using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Declares the owned pbr_post_luma resource contract.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(VanillaGraphicsExpanded.PBR.CameraExposure.IPhotometricFrameBindings), Defaults = true)]
internal interface IPostLumaShaderProgramBindings : VanillaGraphicsExpanded.PBR.CameraExposure.IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Supplies coherent draw parameters.</summary>
    [ShaderBinding("PostprocessInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies sourceImage with explicit edge-clamped sampling.</summary>
    [ShaderBinding("sourceImage", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? SourceImage { set; }
    /// <summary>Supplies published exposure when automatic camera control is enabled.</summary>
    [ShaderBinding("CameraExposure", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Fragment, Required = false)]
    GpuShaderStorageBuffer? ExposureBuffer { set; }
    #endregion
}
