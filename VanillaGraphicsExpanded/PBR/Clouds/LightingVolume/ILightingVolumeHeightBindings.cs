using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Declares the finite native-map reduction into the existing candidate scratch allocation.</summary>
internal interface ILightingVolumeHeightBindings
{
    #region Public API
    /// <summary>Supplies the certified native extent.</summary>
    [ShaderBinding("LightingHeightInputs",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Borrows the current initialized column map without a height hierarchy.</summary>
    [ShaderBinding("heightMap",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Map { set; }
    /// <summary>Writes the reserved reduction record without changing scheduler lists or metadata.</summary>
    [ShaderBinding("HeightScratch",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Scratch { set; }
    #endregion
}
