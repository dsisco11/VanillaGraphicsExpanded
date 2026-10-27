using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.PBR.CameraExposure;
namespace VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
/// <summary>Declares physical scene reduction resources without borrowing generated glare.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations),Defaults=true)]
[ShaderBindingSet(typeof(IPhotometricFrameBindings),Defaults=true)]
internal interface ILocalExposureReduceBindings : IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Samples the complete unexposed scene.</summary>
    [ShaderBinding("sceneRadiance",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef SceneRadiance { set; }
    /// <summary>Receives physical log luminance and explicit validity for every coarse cell.</summary>
    [ShaderBinding("logOutput",ShaderBindingKind.Image,0,ShaderStageKind.Compute)]
    RHIImageBinding LogOutput { set; }
    #endregion
}
