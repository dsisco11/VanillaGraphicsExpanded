using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
/// <summary>Declares every retained input consumed by WorldProbeCommitShader.</summary>
internal interface IWorldProbeCommitShaderBindings
{
    #region Public API
    /// <summary>Retains ResidentAnswers until submission.</summary>
    [ShaderBinding("ResidentAnswers", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuStorageBufferBinding ResidentAnswers { set; }
    /// <summary>Retains Commit until submission.</summary>
    [ShaderBinding("Commit", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuStorageBufferBinding Commit { set; }
    /// <summary>Retains radianceAtlas until submission.</summary>
    [ShaderBinding("radianceAtlas", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    GpuTextureBinding radianceAtlas { set; }
    /// <summary>Retains visibilityAtlas until submission.</summary>
    [ShaderBinding("visibilityAtlas", ShaderBindingKind.Image, 1, ShaderStageKind.Compute)]
    GpuTextureBinding visibilityAtlas { set; }
    /// <summary>Retains distanceAtlas until submission.</summary>
    [ShaderBinding("distanceAtlas", ShaderBindingKind.Image, 2, ShaderStageKind.Compute)]
    GpuTextureBinding distanceAtlas { set; }
    /// <summary>Retains metadataAtlas until submission.</summary>
    [ShaderBinding("metadataAtlas", ShaderBindingKind.Image, 3, ShaderStageKind.Compute)]
    GpuTextureBinding metadataAtlas { set; }
    #endregion
}
