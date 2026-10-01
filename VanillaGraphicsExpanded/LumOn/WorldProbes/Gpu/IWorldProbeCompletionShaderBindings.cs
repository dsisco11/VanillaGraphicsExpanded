using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
/// <summary>Declares every retained input consumed by WorldProbeCompletionShader.</summary>
internal interface IWorldProbeCompletionShaderBindings
{
    #region Public API
    /// <summary>Retains Answers until submission.</summary>
    [ShaderBinding("Answers", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuStorageBufferBinding Answers { set; }
    /// <summary>Retains Completions until submission.</summary>
    [ShaderBinding("Completions", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuStorageBufferBinding Completions { set; }
    /// <summary>Retains Descriptors until submission.</summary>
    [ShaderBinding("Descriptors", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    GpuStorageBufferBinding Descriptors { set; }
    /// <summary>Retains Counter until submission.</summary>
    [ShaderBinding("Counter", ShaderBindingKind.StorageBlock, 3, ShaderStageKind.Compute)]
    GpuStorageBufferBinding Counter { set; }
    #endregion
}
