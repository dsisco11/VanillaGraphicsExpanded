using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Declares the native leaf input and disjoint coarse mip images for cloud reduction.</summary>
internal interface ICloudHierarchyBindings
{
    #region Public API
    /// <summary>Supplies the certified native RGBA32F column map without transferring ownership.</summary>
    [ShaderBinding("cloudMap", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Map { set; }
    /// <summary>Supplies the previous coarse mip; the first dispatch does not read it.</summary>
    [ShaderBinding("sourceBounds", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    GpuTextureBinding Source { set; }
    /// <summary>Supplies the uniquely written destination mip.</summary>
    [ShaderBinding("targetBounds", ShaderBindingKind.Image, 1, ShaderStageKind.Compute)]
    GpuTextureBinding Target { set; }
    /// <summary>Supplies native width, destination width and the native-leaf reduction selector.</summary>
    [ShaderBinding("CloudHierarchyParameters", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    CpuUniformBuffer Parameters { get; }
    #endregion
}
