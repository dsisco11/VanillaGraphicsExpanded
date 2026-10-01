using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for LumonSceneCaptureVoxelComputeShader.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]

internal interface ILumonSceneCaptureVoxelComputeShaderBindings
{
    #region Public API
    /// <summary>Declares the VgeLumOnSceneCaptureVoxelParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnSceneCaptureVoxelParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the vge_depthAtlas Image slot.</summary>
    [ShaderBinding("vge_depthAtlas", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    GpuTextureBinding DepthAtlas { set; }
    /// <summary>Declares the vge_materialAtlas Image slot.</summary>
    [ShaderBinding("vge_materialAtlas", ShaderBindingKind.Image, 1, ShaderStageKind.Compute)]
    GpuTextureBinding MaterialAtlas { set; }
    /// <summary>Declares the VgeCaptureWork StorageBlock slot.</summary>
    [ShaderBinding("VgeCaptureWork", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer CaptureWork { set; }
    /// <summary>Declares the VgePatchMetadata StorageBlock slot.</summary>
    [ShaderBinding("VgePatchMetadata", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer PatchMetadata { set; }
    /// <summary>Declares the VgeChunkSlotInfo StorageBlock slot.</summary>
    [ShaderBinding("VgeChunkSlotInfo", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer ChunkSlotInfo { set; }
    /// <summary>Retains only the diagnostics storage admitted for this dispatch.</summary>
    [ShaderBinding("SurfaceDiagnostics", ShaderBindingKind.StorageBlock, 4, ShaderStageKind.Compute, Required = false)]
    GpuShaderStorageBuffer? DiagnosticCounters { get; }
    #endregion
}
