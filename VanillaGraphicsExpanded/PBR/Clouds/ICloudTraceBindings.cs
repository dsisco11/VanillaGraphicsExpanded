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
    /// <summary>Supplies hardware-filtered compact shadow parameters; source admission is separate in traversal inputs.</summary>
    [ShaderBinding("cloudShadowCache",ShaderBindingKind.Sampler,10,ShaderStageKind.Fragment,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.LinearClamp,Required=false)]
    GpuTexture? ShadowCache { set; }
    #endregion
}
