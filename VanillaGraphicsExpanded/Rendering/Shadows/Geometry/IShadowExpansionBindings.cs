using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Eight-block baseline interface for candidate-driven bounded terrain command generation.</summary>
internal interface IShadowExpansionBindings
{
    #region Public API
    [ShaderBinding("DrawInputs", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Inputs { set; }
    [ShaderBinding("DrawWorld", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? World { set; }
    [ShaderBinding("DrawRanges", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Ranges { set; }
    [ShaderBinding("DrawCandidates", ShaderBindingKind.StorageBlock, 3, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Candidates { set; }
    [ShaderBinding("DrawCoordinates", ShaderBindingKind.StorageBlock, 4, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Coordinates { set; }
    [ShaderBinding("DrawPages", ShaderBindingKind.StorageBlock, 5, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Pages { set; }
    [ShaderBinding("DrawScratch", ShaderBindingKind.StorageBlock, 6, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Scratch { set; }
    [ShaderBinding("DrawOutput", ShaderBindingKind.StorageBlock, 7, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Output { set; }
    #endregion
}
