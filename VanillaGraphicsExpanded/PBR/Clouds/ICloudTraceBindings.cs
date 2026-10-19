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
    /// <summary>Supplies eight spatial rank masks for frame-indexed primary ray offsets.</summary>
    [ShaderBinding("cloudPrimaryNoise",ShaderBindingKind.Sampler,9,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? PrimaryNoise { set; }
    /// <summary>Supplies certified coarse height summaries only to reduced view tracing.</summary>
    [ShaderBinding("cloudHeightBounds",ShaderBindingKind.Sampler,8,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp,Required=false)]
    GpuTexture? HeightBounds { set; }
    /// <summary>Supplies same-frame light-space attenuation; availability is published separately in traversal inputs.</summary>
    [ShaderBinding("cloudShadowCache",ShaderBindingKind.Sampler,10,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.NearestClamp,Required=false)]
    GpuTexture? ShadowCache { set; }
    /// <summary>Supplies root-bound availability; zero availability uses native support at the same shadow midpoints.</summary>
    [ShaderBinding("CloudTraversalParameters",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Material,ShaderStageKind.Fragment)]
    CpuUniformBuffer TraversalInputs { get; }
    #endregion
}
