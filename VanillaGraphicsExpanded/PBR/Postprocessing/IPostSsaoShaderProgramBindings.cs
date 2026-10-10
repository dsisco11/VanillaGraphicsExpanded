using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Declares resources consumed by pbr_post_ssao.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface IPostSsaoShaderProgramBindings
{
    #region Public API
    /// <summary>Borrows the shared world/view camera block.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Supplies the coherent draw parameter block.</summary>
    [ShaderBinding("AmbientOcclusionInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies depthImage through the retained typed texture contract.</summary>
    [ShaderBinding("depthImage", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? DepthImage { set; }
    /// <summary>Supplies surfaceImage through the retained typed texture contract.</summary>
    [ShaderBinding("surfaceImage", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? SurfaceImage { set; }
    /// <summary>Supplies the shared hardware-depth hierarchy with explicit shader LOD selection.</summary>
    [ShaderBinding("depthHierarchy", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? DepthHierarchy { set; }
    #endregion
}
