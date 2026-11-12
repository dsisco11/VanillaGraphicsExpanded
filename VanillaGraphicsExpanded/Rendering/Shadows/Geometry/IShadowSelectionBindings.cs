using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Declares bounded selection inputs and GPU-only indirect output on the baseline compute interface.</summary>
internal interface IShadowSelectionBindings
{
    #region Public API
    /// <summary>Frozen view, association and aggregate policy records.</summary>
    [ShaderBinding("SelectionInputs", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Inputs { set; }
    /// <summary>Current dimension-aware native chunk lookup.</summary>
    [ShaderBinding("SelectionWorld", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? World { set; }
    /// <summary>Association completion and dependency certificates.</summary>
    [ShaderBinding("SelectionHeaders", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Headers { set; }
    /// <summary>Complete conservative chunk coordinate spans.</summary>
    [ShaderBinding("SelectionCoordinates", ShaderBindingKind.StorageBlock, 3, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Coordinates { set; }
    /// <summary>Current native ranges resolved through the authoritative drawing groups.</summary>
    [ShaderBinding("SelectionRanges", ShaderBindingKind.StorageBlock, 4, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Ranges { set; }
    /// <summary>Initialized indirect commands, compact selectors and a whole-batch completion certificate.</summary>
    [ShaderBinding("SelectionOutput", ShaderBindingKind.StorageBlock, 5, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Output { set; }
    #endregion
}
