using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
/// <summary>Declares every retained input consumed by WorldProbeTraceShader.</summary>
internal interface IWorldProbeTraceShaderBindings : VanillaGraphicsExpanded.LumOn.Scene.Shaders.ISurfaceLightingInputs
{
    #region Public API
    /// <summary>Retains WorldProbeAnswers until submission.</summary>
    [ShaderBinding("WorldProbeAnswers", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuStorageBufferBinding WorldProbeAnswers { set; }
    /// <summary>Retains WorldProbeTraceProbes until submission.</summary>
    [ShaderBinding("WorldProbeTraceProbes", ShaderBindingKind.StorageBlock, 4, ShaderStageKind.Compute)]
    GpuStorageBufferBinding WorldProbeTraceProbes { set; }
    /// <summary>Retains WorldProbeTraceTiles until submission.</summary>
    [ShaderBinding("WorldProbeTraceTiles", ShaderBindingKind.StorageBlock, 5, ShaderStageKind.Compute)]
    GpuStorageBufferBinding WorldProbeTraceTiles { set; }
    /// <summary>Retains WorldProbeSelectedDirections until submission.</summary>
    [ShaderBinding("WorldProbeSelectedDirections", ShaderBindingKind.StorageBlock, 6, ShaderStageKind.Compute)]
    GpuStorageBufferBinding WorldProbeSelectedDirections { set; }
    #endregion
}
