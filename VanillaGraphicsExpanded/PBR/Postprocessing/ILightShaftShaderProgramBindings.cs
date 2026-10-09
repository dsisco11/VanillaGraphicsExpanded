using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Declares the owned pbr_lightshafts resource contract.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface ILightShaftShaderProgramBindings
{
    #region Public API
    /// <summary>Reuses the shared projection for scene-depth reconstruction.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Supplies coherent draw parameters.</summary>
    [ShaderBinding("PostprocessInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies visibilityImage with explicit edge-clamped sampling.</summary>
    [ShaderBinding("visibilityImage", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? VisibilityImage { set; }
    /// <summary>Supplies depthImage with explicit edge-clamped sampling.</summary>
    [ShaderBinding("depthImage", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? DepthImage { set; }
    /// <summary>Supplies HDR scene radiance or the preceding radial result.</summary>
    [ShaderBinding("sourceImage", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? SourceImage { set; }
    /// <summary>Supplies the shared camera exposure without exposing the stored scene.</summary>
    [ShaderBinding("exposureImage", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? ExposureImage { set; }
    #endregion
}
