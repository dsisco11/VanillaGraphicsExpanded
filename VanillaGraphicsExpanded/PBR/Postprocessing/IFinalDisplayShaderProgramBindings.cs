using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Declares typed resources for owned final composition.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(VanillaGraphicsExpanded.PBR.CameraExposure.IPhotometricFrameBindings), Defaults = true)]
internal interface IFinalDisplayShaderProgramBindings : VanillaGraphicsExpanded.PBR.CameraExposure.IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Supplies the shared frame dimensions.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
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
    /// <summary>Supplies the matching cached grading, film response and encoded display transform.</summary>
    [ShaderBinding("gradedDisplayLookup",ShaderBindingKind.Sampler,3,ShaderStageKind.Fragment,
        TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.LinearClamp)]
    DynamicTexture3D? GradingLookup { set; }
    /// <summary>Supplies the published exposure buffer without transferring ownership.</summary>
    [ShaderBinding("CameraExposure", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Fragment, Required = false)]
    GpuShaderStorageBuffer? ExposureBuffer { set; }
    /// <summary>Supplies the current coarse physical base only to the optional local variant.</summary>
    [ShaderBinding("localBaseImage",ShaderBindingKind.Sampler,4,ShaderStageKind.Fragment,Required=false,
        TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? LocalBase { set; }
    #endregion
}
