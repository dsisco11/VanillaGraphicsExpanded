using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Storage-only GPU allocation interface shared with future GPU demand producers.</summary>
internal interface IShadowPageBindings
{
    #region Public API
    /// <summary>Dispatch policy and one bounded operation.</summary>
    [ShaderBinding("PageCommands", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Commands { set; }
    /// <summary>Physical assignments, certification and table-backed free/retirement queues.</summary>
    [ShaderBinding("PageEntries", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Entries { set; }
    /// <summary>GPU result and monotonic assignment counter; no production readback is required.</summary>
    [ShaderBinding("PageResults", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Results { set; }
    #endregion
}
