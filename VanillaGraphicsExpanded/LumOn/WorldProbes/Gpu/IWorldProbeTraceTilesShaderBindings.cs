using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
/// <summary>Declares every retained input consumed by WorldProbeTraceTilesShader.</summary>
internal interface IWorldProbeTraceTilesShaderBindings
{
    #region Public API
    /// <summary>Retains WorldProbeTraceProbes until submission.</summary>
    [ShaderBinding("WorldProbeTraceProbes", ShaderBindingKind.StorageBlock, 4, ShaderStageKind.Compute)]
    GpuStorageBufferBinding WorldProbeTraceProbes { set; }
    /// <summary>Retains WorldProbeTraceTiles until submission.</summary>
    [ShaderBinding("WorldProbeTraceTiles", ShaderBindingKind.StorageBlock, 5, ShaderStageKind.Compute)]
    GpuStorageBufferBinding WorldProbeTraceTiles { set; }
    /// <summary>Retains WorldProbeTraceDispatch until submission.</summary>
    [ShaderBinding("WorldProbeTraceDispatch", ShaderBindingKind.StorageBlock, 7, ShaderStageKind.Compute)]
    GpuStorageBufferBinding WorldProbeTraceDispatch { set; }
    #endregion
}
