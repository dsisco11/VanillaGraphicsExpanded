using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Extends the physical volume inputs with explicit reduced-view scheduling.</summary>
[ShaderBindingSet(typeof(ICloudVolumeBindings),Defaults=true)]
internal interface ICloudTraceBindings : ICloudVolumeBindings
{
    #region Public API
    /// <summary>Supplies reprojection, lifecycle admission and exact reduced trace phases.</summary>
    [ShaderBinding("CloudTemporalParameters",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Object,ShaderStageKind.Fragment)]
    CpuUniformBuffer TemporalInputs { get; }
    /// <summary>Supplies cloud-owned scrambled PMJ offsets without depending on LumOn resource availability.</summary>
    [ShaderBinding("cloudPmjJitter",ShaderBindingKind.Sampler,7,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? PmjJitter { set; }
    /// <summary>Supplies eight spatial rank masks for frame-indexed primary ray offsets.</summary>
    [ShaderBinding("cloudPrimaryNoise",ShaderBindingKind.Sampler,9,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? PrimaryNoise { set; }
    /// <summary>Supplies certified coarse height summaries only to reduced view tracing.</summary>
    [ShaderBinding("cloudHeightBounds",ShaderBindingKind.Sampler,8,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp,Required=false)]
    GpuTexture? HeightBounds { set; }
    /// <summary>Supplies explicit availability and the root owned mip; zero availability preserves native-column DDA.</summary>
    [ShaderBinding("CloudTraversalParameters",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Material,ShaderStageKind.Fragment)]
    CpuUniformBuffer TraversalInputs { get; }
    #endregion
}
