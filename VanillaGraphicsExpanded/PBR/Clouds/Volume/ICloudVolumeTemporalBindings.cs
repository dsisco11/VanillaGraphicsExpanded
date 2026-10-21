using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Declares disjoint current and previous compact physical view samples.</summary>
[ShaderBindingSet(typeof(ICloudVolumeReconstructBindings),Defaults=true)]
internal interface ICloudVolumeTemporalBindings:ICloudVolumeReconstructBindings
{
    #region Public API
    /// <summary>Supplies shared camera/material reprojection and history admission.</summary>
    [ShaderBinding("CloudTemporalParameters",ShaderBindingKind.UniformBlock,14,ShaderStageKind.Fragment)]
    CpuUniformBuffer TemporalInputs {get;}
    /// <summary>Reads the previous physical radiance and opacity with hardware filtering.</summary>
    [ShaderBinding("previousCloud",ShaderBindingKind.Sampler,6,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? PreviousCloud {set;}
    /// <summary>Reads all previous bucket factors with the same filtering position.</summary>
    [ShaderBinding("previousBins",ShaderBindingKind.Sampler,7,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? PreviousBins {set;}
    /// <summary>Checks exact previous depth, completion and history confidence before filtering.</summary>
    [ShaderBinding("previousMetadata",ShaderBindingKind.Sampler,8,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? PreviousMetadata {set;}
    /// <summary>Maps compact raw texels to the same blue-noise-selected reduced pixels as tracing.</summary>
    [ShaderBinding("temporalBlueNoise",ShaderBindingKind.Sampler,9,ShaderStageKind.Fragment,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? BlueNoise {set;}
    #endregion
}
