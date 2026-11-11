using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Pairs the GPU allocation token with explicit writable integer banks.</summary>
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
    /// <summary>First integer tile bank.</summary>
    [ShaderBinding("clearBank0", ShaderBindingKind.Image, 0, ShaderStageKind.Compute, ShaderType = ShaderResourceType.UnsignedIntImage2D)]
    RHIImageBinding Bank0 { set; }
    /// <summary>Second integer tile bank.</summary>
    [ShaderBinding("clearBank1", ShaderBindingKind.Image, 1, ShaderStageKind.Compute, ShaderType = ShaderResourceType.UnsignedIntImage2D)]
    RHIImageBinding Bank1 { set; }
    /// <summary>Third integer tile bank.</summary>
    [ShaderBinding("clearBank2", ShaderBindingKind.Image, 2, ShaderStageKind.Compute, ShaderType = ShaderResourceType.UnsignedIntImage2D)]
    RHIImageBinding Bank2 { set; }
    /// <summary>Fourth integer tile bank.</summary>
    [ShaderBinding("clearBank3", ShaderBindingKind.Image, 3, ShaderStageKind.Compute, ShaderType = ShaderResourceType.UnsignedIntImage2D)]
    RHIImageBinding Bank3 { set; }
    #endregion
}
