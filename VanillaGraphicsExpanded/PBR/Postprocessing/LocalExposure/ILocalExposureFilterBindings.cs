using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
/// <summary>Declares disjoint reduction input and filtered base output.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations),Defaults=true)]
internal interface ILocalExposureFilterBindings
{
    #region Public API
    /// <summary>Supplies the bounded candidate footprint.</summary>
    [ShaderBinding("LocalExposureInputs",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.ShaderInputs,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { set; }
    /// <summary>Supplies the completed physical log grid.</summary>
    [ShaderBinding("logImage",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef LogImage { set; }
    /// <summary>Receives the current-frame edge-preserving base and validity.</summary>
    [ShaderBinding("baseOutput",ShaderBindingKind.Image,0,ShaderStageKind.Compute)]
    RHIImageBinding BaseOutput { set; }
    #endregion
}
