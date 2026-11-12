using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Sampling;

/// <summary>One integer array sampler and bounded validity metadata for shared PCF.</summary>
internal interface IShadowSamplingBindings
{
    #region Public API
    /// <summary>Projection, world receiver, bias and required scene publication.</summary>
    [ShaderBinding("ShadowSampleInputs", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Inputs { set; }
    /// <summary>Certified page metadata owned by the shared pool.</summary>
    [ShaderBinding("PageEntries", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Entries { set; }
    /// <summary>Visibility and diagnosed invalid-tap mask, plus nine tap addresses for demand integration.</summary>
    [ShaderBinding("ShadowSampleResults", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Results { set; }
    /// <summary>Complete integer depth array with explicit layer/tile addressing.</summary>
    [ShaderBinding("shadowDepthPool", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, ShaderType = ShaderResourceType.UnsignedIntSampler2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef Depth { set; }
    #endregion
}
