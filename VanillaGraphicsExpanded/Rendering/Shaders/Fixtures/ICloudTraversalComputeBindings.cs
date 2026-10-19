using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares retained inputs and output for the offline cloud traversal fixture.</summary>
internal interface ICloudTraversalComputeBindings
{
    #region Public API
    /// <summary>Supplies native column data through the ordinary sampler publication path.</summary>
    [ShaderBinding("cloudMap", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Map { set; }
    /// <summary>Supplies compact records through the production hardware-linear sampler policy.</summary>
    [ShaderBinding("shadowRecordMap", ShaderBindingKind.Sampler, 10, ShaderStageKind.Compute, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? ShadowRecord { set; }
    /// <summary>Supplies optional certified height summaries without requiring them on the direct path.</summary>
    [ShaderBinding("cloudHeightBounds", ShaderBindingKind.Sampler, 8, ShaderStageKind.Compute, Required = false, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? HeightBounds { set; }
    /// <summary>Publishes the caller-owned cloud density and lighting parameters.</summary>
    [ShaderBinding("CloudVolumeParameters", ShaderBindingKind.UniformBlock, 28, ShaderStageKind.Compute)]
    CpuUniformBuffer VolumeInputs { get; }
    /// <summary>Publishes root availability and mip identity.</summary>
    [ShaderBinding("CloudTraversalParameters", ShaderBindingKind.UniformBlock, 15, ShaderStageKind.Compute)]
    CpuUniformBuffer TraversalInputs { get; }
    /// <summary>Publishes the authored ray and diagnostic selector.</summary>
    [ShaderBinding("CloudRayTestInputs", ShaderBindingKind.UniformBlock, 16, ShaderStageKind.Compute)]
    CpuUniformBuffer RayInputs { get; }
    /// <summary>Retains the storage output for guarded dispatch publication.</summary>
    [ShaderBinding("CloudTraversalTestResult", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Result { set; }
    #endregion
}
