using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Declares the source-certified cloud-owned light-space generation resources.</summary>
internal interface ICloudShadowAttenuationComputeBindings
{
    #region Public API
    /// <summary>Borrows native column controls through the ordinary nearest sampler.</summary>
    [ShaderBinding("cloudMap",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Map { set; }
    /// <summary>Borrows certified root heights for complete finite-domain coverage.</summary>
    [ShaderBinding("cloudHeightBounds",ShaderBindingKind.Sampler,8,ShaderStageKind.Compute,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? HeightBounds { set; }
    /// <summary>Publishes the identical density, placement and physical sources used for tracing.</summary>
    [ShaderBinding("CloudVolumeParameters",ShaderBindingKind.UniformBlock,28,ShaderStageKind.Compute)]
    CpuUniformBuffer VolumeInputs { get; }
    /// <summary>Publishes summary availability and mip identity.</summary>
    [ShaderBinding("CloudTraversalParameters",ShaderBindingKind.UniformBlock,15,ShaderStageKind.Compute)]
    CpuUniformBuffer TraversalInputs { get; }
    /// <summary>Publishes cache width, quadrature count and the selected source.</summary>
    [ShaderBinding("CloudShadowCacheInputs",ShaderBindingKind.UniformBlock,16,ShaderStageKind.Compute)]
    CpuUniformBuffer CacheInputs { get; }
    /// <summary>Borrows the disjoint RGBA32F moment image owned by the cloud cache.</summary>
    [ShaderBinding("shadowCacheTarget",ShaderBindingKind.Image,0,ShaderStageKind.Compute)]
    GpuTextureBinding Target { set; }
    #endregion
}
