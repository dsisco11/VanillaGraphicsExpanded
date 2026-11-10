using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>Declares the GL 4.5 storage-only traversal interface without optional draw capabilities.</summary>
internal interface IShadowCandidateBindings
{
    #region Public API
    /// <summary>Supplies bounded relative coverage, sector slopes and current dependencies.</summary>
    [ShaderBinding("CandidateInputs", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Inputs { set; }
    /// <summary>Supplies continuation cursors and publication state.</summary>
    [ShaderBinding("CandidateHeaders", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Headers { set; }
    /// <summary>Supplies fixed-size coordinate segments owned by the allocator.</summary>
    [ShaderBinding("CandidateCoordinates", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Coordinates { set; }
    /// <summary>Supplies bounded per-association hash storage.</summary>
    [ShaderBinding("CandidateDedup", ShaderBindingKind.StorageBlock, 3, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Dedup { set; }
    /// <summary>Supplies the current contiguous changed-partition interval.</summary>
    [ShaderBinding("CandidateChanges", ShaderBindingKind.StorageBlock, 4, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Changes { set; }
    /// <summary>Supplies the current native coordinate lookup, including advisory classification.</summary>
    [ShaderBinding("CandidateWorld", ShaderBindingKind.StorageBlock, 5, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? World { set; }
    #endregion
}
