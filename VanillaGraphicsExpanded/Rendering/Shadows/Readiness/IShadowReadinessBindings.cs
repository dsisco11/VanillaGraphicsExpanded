using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Readiness;

/// <summary>Bounded whole-light page reduction and persistent recovery state.</summary>
internal interface IShadowReadinessBindings
{
    /// <summary>Complete current demand and selected rendered mappings.</summary>
    [ShaderBinding("ShadowReadinessInputs", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Inputs { set; }
    /// <summary>Completed physical assignments and depth-inclusive coverage.</summary>
    [ShaderBinding("PageEntries", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Entries { set; }
    /// <summary>Current, permitted-stale or unavailable state per whole light.</summary>
    [ShaderBinding("ShadowReadinessResults", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Results { set; }
    /// <summary>Stable-source consecutive recovery state.</summary>
    [ShaderBinding("ShadowReadinessHistory", ShaderBindingKind.StorageBlock, 3, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? History { set; }
    /// <summary>Compact delayed eligibility feedback for CPU priority hints; never authorizes sampling.</summary>
    [ShaderBinding("ShadowPriorityResults",ShaderBindingKind.StorageBlock,4,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Priority { set; }
    /// <summary>Resident storage dimensions, validated against every completed assignment.</summary>
    [ShaderBinding("shadowDepthPool", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, ShaderType=ShaderResourceType.UnsignedIntSampler2DArray, Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef Depth { set; }
}
