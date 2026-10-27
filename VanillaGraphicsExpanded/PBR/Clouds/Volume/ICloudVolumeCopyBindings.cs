using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Declares disjoint diffuse-copy source and destination with atomic field/scale publication.</summary>
internal interface ICloudVolumeCopyBindings
{
    #region Public API
    /// <summary>Supplies dimensions, encoding scales and physical destination bounds.</summary>
    [ShaderBinding("VolumeCopy", ShaderBindingKind.UniformBlock, 0, ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Reads the still-published source by exact texel address.</summary>
    [ShaderBinding("oldDiffuse", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D, Sampler=ShaderSamplerPolicy.Default)]
    GpuTextureRef Source { set; }
    /// <summary>Writes only the unpublished spare.</summary>
    [ShaderBinding("newDiffuse", ShaderBindingKind.Image, 0, ShaderStageKind.Compute, TextureTarget=ShaderTextureTarget.Texture3D)]
    RHIImageBinding Destination { set; }
    /// <summary>Retains previous successful block indices and records copied texels in the existing deferred status.</summary>
    [ShaderBinding("CopyScratch", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Scratch { set; }
    #endregion
}
