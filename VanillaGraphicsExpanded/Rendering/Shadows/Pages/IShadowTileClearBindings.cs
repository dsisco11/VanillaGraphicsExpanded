using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Pairs the GPU allocation token with the writable integer texture array.</summary>
internal interface IShadowTileClearBindings
{
    #region Public API
    /// <summary>Pool dimensions and ordered clear/finalize operation.</summary>
    [ShaderBinding("ClearPolicy", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Policy { set; }
    /// <summary>Assignment ownership and writable state.</summary>
    [ShaderBinding("PageEntries", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Entries { set; }
    /// <summary>Exact GPU-selected allocation token.</summary>
    [ShaderBinding("PageResults", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Results { set; }
    /// <summary>Complete layered destination; the selected assignment determines layer and tile.</summary>
    [ShaderBinding("clearDepthPool", ShaderBindingKind.Image, 0, ShaderStageKind.Compute, ShaderType = ShaderResourceType.UnsignedIntImage2DArray)]
    RHIImageBinding Depth { set; }
    #endregion
}
