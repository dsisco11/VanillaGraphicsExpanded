using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Declares the independent view's camera, physical lighting fields, depths and native output inputs.</summary>
[ShaderBindingSet(typeof(IPhotometricFrameBindings),Defaults=true)]
internal interface ICloudVolumeViewBindings:IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Supplies this view's complete camera publication.</summary>
    [ShaderBinding("VgeFrameUBO",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Frame,ShaderStageKind.Fragment)]
    CpuUniformBuffer Camera { get; }
    /// <summary>Supplies finite stored geometry, physical sources and medium.</summary>
    [ShaderBinding("StoredVolumeView",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Fragment)]
    CpuUniformBuffer View {get;}
    /// <summary>Reads conservative stored-density occupancy.</summary>
    [ShaderBinding("VolumeOccupancy",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Fragment)]
    GpuShaderStorageBuffer? Occupancy {set;}
    /// <summary>Reads exact region ages/identity only for the developer overlay.</summary>
    [ShaderBinding("ViewRegions",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Fragment)]
    GpuShaderStorageBuffer? Regions {set;}
    /// <summary>Fetches authoritative constant-cell extinction by exact physical texel address.</summary>
    [ShaderBinding("volumeDensity",ShaderBindingKind.Sampler,0,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTexture? Density {set;}
    /// <summary>Samples retained solar/lunar transmission in the compiled shadow path.</summary>
    [ShaderBinding("viewDirect",ShaderBindingKind.Sampler,1,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTexture? Direct {set;}
    /// <summary>Samples the published diffuse field with its paired scale.</summary>
    [ShaderBinding("viewDiffuse",ShaderBindingKind.Sampler,2,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture3D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTexture? DiffuseField {set;}
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
