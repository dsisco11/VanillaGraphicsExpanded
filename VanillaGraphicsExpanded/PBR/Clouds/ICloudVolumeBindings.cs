using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Declares only the same-frame map, current view depths and completed physical atmosphere.</summary>
[ShaderBindingSet(typeof(CameraExposure.IPhotometricFrameBindings), Defaults = true)]
internal interface ICloudVolumeBindings : CameraExposure.IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Shares one static periodic volume; object filtering and repeat apply without a sampler override.</summary>
    [ShaderBinding("cloudPerturbation",ShaderBindingKind.Sampler,11,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTexture? Perturbation { set; }
    /// <summary>Supplies a complete camera contract, independently selectable for controlled views.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Supplies the admitted map placement and lighting.</summary>
    [ShaderBinding("CloudVolumeParameters", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Borrows certified layer bounds independently of optional traversal acceleration.</summary>
    [ShaderBinding("cloudHeightBounds",ShaderBindingKind.Sampler,8,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp,Required=false)]
    GpuTexture? HeightBounds { set; }
    /// <summary>Publishes current layer-bound availability and root identity for both primary paths.</summary>
    [ShaderBinding("CloudTraversalParameters",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Material,ShaderStageKind.Fragment)]
    CpuUniformBuffer TraversalInputs { get; }
    /// <summary>Borrows initialized native column data without binding its pre-lit color texture.</summary>
    [ShaderBinding("cloudMap", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int Map { set; }
    /// <summary>Borrows mip zero of the same-frame shared material-depth hierarchy.</summary>
    [ShaderBinding("sceneDepth", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int SceneDepth { set; }
    /// <summary>Retains primary particle visibility independently of corrected material depth.</summary>
    [ShaderBinding("visibilityDepth",ShaderBindingKind.Sampler,6,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    int VisibilityDepth { set; }
    /// <summary>Clips against this view's liquid surface using normalized view coordinates.</summary>
    [ShaderBinding("liquidDepth", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int LiquidDepth { set; }
    /// <summary>Uses the matching encoded aerial bands.</summary>
    [ShaderBinding("vge_atmosphereAerialRadiance", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture3D)]
    DynamicTexture3D? AerialRadiance { set; }
    /// <summary>Uses dimensionless aerial loss.</summary>
    [ShaderBinding("vge_atmosphereAerialAttenuation", ShaderBindingKind.Sampler, 4, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture3D)]
    DynamicTexture3D? AerialAttenuation { set; }
    /// <summary>Modulates only atmospheric in-scattering through the existing shaft owner.</summary>
    [ShaderBinding("vge_lightShaftOcclusion", ShaderBindingKind.Sampler, 5, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.LinearClamp, Required = false)]
    GpuTexture? LightShafts { set; }
    #endregion
}
