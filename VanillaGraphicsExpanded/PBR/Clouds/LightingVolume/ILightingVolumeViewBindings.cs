using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Declares the independent view's camera, physical lighting fields, depths and native output inputs.</summary>
[ShaderBindingSet(typeof(IPhotometricFrameBindings),Defaults=true)]
internal interface ILightingVolumeViewBindings:IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Supplies this view's complete camera publication.</summary>
    [ShaderBinding("VgeFrameUBO",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Frame,ShaderStageKind.Fragment)]
    CpuUniformBuffer Camera { get; }
    /// <summary>Supplies bounded native/grid coordinates and physical diffuse decoding.</summary>
    [ShaderBinding("LightingVolumeInputs",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Fragment)]
    CpuUniformBuffer Grid { get; }
    /// <summary>Supplies the frozen physical sources, view budget and local medium.</summary>
    [ShaderBinding("LightingViewInputs",ShaderBindingKind.UniformBlock,1,ShaderStageKind.Fragment)]
    CpuUniformBuffer View { get; }
    /// <summary>Reads the same-publication native height envelope from reserved candidate scratch.</summary>
    [ShaderBinding("ViewScratch",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Fragment)]
    GpuShaderStorageBuffer? Scratch { set; }
    /// <summary>Borrows native density controls without pre-lit cloud color.</summary>
    [ShaderBinding("candidateMap",ShaderBindingKind.Sampler,0,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Map { set; }
    /// <summary>Uses one hardware trilinear direct lookup for each valid source footprint.</summary>
    [ShaderBinding("candidateDirect",ShaderBindingKind.Sampler,1,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? Direct { set; }
    /// <summary>Reads exact node masks and hardware trilinear physical diffuse RGB.</summary>
    [ShaderBinding("candidateDiffuse",ShaderBindingKind.Sampler,2,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? DiffuseField { set; }
    /// <summary>Clips to material depth at explicit mip zero.</summary>
    [ShaderBinding("viewMaterialDepth",ShaderBindingKind.Sampler,3,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? MaterialDepth { set; }
    /// <summary>Preserves foreground-particle visibility independently of material correction.</summary>
    [ShaderBinding("viewVisibilityDepth",ShaderBindingKind.Sampler,4,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? VisibilityDepth { set; }
    /// <summary>Clips to liquid depth using normalized full-view coordinates.</summary>
    [ShaderBinding("viewLiquidDepth",ShaderBindingKind.Sampler,5,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? LiquidDepth { set; }
    /// <summary>Borrows the matching completed aerial radiance publication.</summary>
    [ShaderBinding("vge_atmosphereAerialRadiance",ShaderBindingKind.Sampler,6,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture3D)]
    DynamicTexture3D? Aerial { set; }
    /// <summary>Borrows dimensionless aerial attenuation.</summary>
    [ShaderBinding("vge_atmosphereAerialAttenuation",ShaderBindingKind.Sampler,7,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture3D)]
    DynamicTexture3D? Attenuation { set; }
    /// <summary>Modulates incoming atmosphere through the existing optional shaft publication.</summary>
    [ShaderBinding("vge_lightShaftOcclusion",ShaderBindingKind.Sampler,8,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.LinearClamp,Required=false)]
    GpuTexture? Shafts { set; }
    #endregion
}
