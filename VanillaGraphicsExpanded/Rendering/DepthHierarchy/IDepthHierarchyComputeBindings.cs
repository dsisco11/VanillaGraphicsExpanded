using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Declares retained inputs and explicit mip views for shared depth generation.</summary>
internal interface IDepthHierarchyComputeBindings
{
    #region Public API
    /// <summary>Supplies the corrected receiver depth.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? PrimaryDepth { set; }
    /// <summary>Supplies the full extent and mip count.</summary>
    [ShaderBinding("VgeDepthHierarchyComputeUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Supplies the completion counter and compact coarse-tail storage.</summary>
    [ShaderBinding("DepthHierarchyTail", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Tail { set; }
    /// <summary>Supplies the explicit level-0 output view.</summary>
    [ShaderBinding("mip0", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    GpuTextureBinding Mip0 { set; }
    /// <summary>Supplies the explicit level-1 output view.</summary>
    [ShaderBinding("mip1", ShaderBindingKind.Image, 1, ShaderStageKind.Compute)]
    GpuTextureBinding Mip1 { set; }
    /// <summary>Supplies the explicit level-2 output view.</summary>
    [ShaderBinding("mip2", ShaderBindingKind.Image, 2, ShaderStageKind.Compute)]
    GpuTextureBinding Mip2 { set; }
    /// <summary>Supplies the explicit level-3 output view.</summary>
    [ShaderBinding("mip3", ShaderBindingKind.Image, 3, ShaderStageKind.Compute)]
    GpuTextureBinding Mip3 { set; }
    /// <summary>Supplies the explicit level-4 output view.</summary>
    [ShaderBinding("mip4", ShaderBindingKind.Image, 4, ShaderStageKind.Compute)]
    GpuTextureBinding Mip4 { set; }
    /// <summary>Supplies the explicit level-5 output view.</summary>
    [ShaderBinding("mip5", ShaderBindingKind.Image, 5, ShaderStageKind.Compute)]
    GpuTextureBinding Mip5 { set; }
    /// <summary>Supplies the explicit level-6 output view.</summary>
    [ShaderBinding("mip6", ShaderBindingKind.Image, 6, ShaderStageKind.Compute)]
    GpuTextureBinding Mip6 { set; }
    #endregion
}
