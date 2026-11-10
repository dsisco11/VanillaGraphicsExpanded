using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Sampling;

/// <summary>Four statically named nearest samplers and bounded validity metadata for shared PCF.</summary>
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
    /// <summary>First statically addressed array.</summary>
    [ShaderBinding("shadowDepth0", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, ShaderType = ShaderResourceType.Sampler2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef Depth0 { set; }
    /// <summary>Second statically addressed array.</summary>
    [ShaderBinding("shadowDepth1", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, ShaderType = ShaderResourceType.Sampler2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef Depth1 { set; }
    /// <summary>Third statically addressed array.</summary>
    [ShaderBinding("shadowDepth2", ShaderBindingKind.Sampler, 2, ShaderStageKind.Compute, ShaderType = ShaderResourceType.Sampler2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef Depth2 { set; }
    /// <summary>Fourth statically addressed array.</summary>
    [ShaderBinding("shadowDepth3", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute, ShaderType = ShaderResourceType.Sampler2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef Depth3 { set; }
    #endregion
}
