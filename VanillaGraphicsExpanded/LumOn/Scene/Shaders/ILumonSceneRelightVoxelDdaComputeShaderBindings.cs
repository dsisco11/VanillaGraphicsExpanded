using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for LumonSceneRelightVoxelDdaComputeShader.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]

internal interface ILumonSceneRelightVoxelDdaComputeShaderBindings
{
    #region Public API
    /// <summary>Reuses the shared integer rendering frame for randomized sampling.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Compute)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Declares the VgeLumOnSceneRelightParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnSceneRelightParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the vge_depthAtlas Sampler slot.</summary>
    [ShaderBinding("vge_depthAtlas", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int DepthAtlas { set; }
    /// <summary>Declares the vge_materialAtlas Sampler slot.</summary>
    [ShaderBinding("vge_materialAtlas", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int MaterialAtlas { set; }
    /// <summary>Declares the vge_lightColorLut Sampler slot.</summary>
    [ShaderBinding("vge_lightColorLut", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int LightColorLut { set; }
    /// <summary>Declares the vge_blockLevelScalarLut Sampler slot.</summary>
    [ShaderBinding("vge_blockLevelScalarLut", ShaderBindingKind.Sampler, 4, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int BlockLevelScalarLut { set; }
    /// <summary>Declares the vge_sunLevelScalarLut Sampler slot.</summary>
    [ShaderBinding("vge_sunLevelScalarLut", ShaderBindingKind.Sampler, 5, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int SunLevelScalarLut { set; }
    /// <summary>Declares the vge_surfaceLut Sampler slot.</summary>
    [ShaderBinding("vge_surfaceLut", ShaderBindingKind.Sampler, 7, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int SurfaceLut { set; }
    /// <summary>Declares the vge_irradianceAtlas Image slot.</summary>
    [ShaderBinding("vge_irradianceAtlas", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    GpuTextureBinding IrradianceAtlas { set; }
    /// <summary>Declares the VgeRelightWork StorageBlock slot.</summary>
    [ShaderBinding("VgeRelightWork", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer RelightWork { set; }
    /// <summary>Declares the VgePatchMetadata StorageBlock slot.</summary>
    [ShaderBinding("VgePatchMetadata", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer PatchMetadata { set; }
    /// <summary>Retains diagnostic counters without clearing their contents.</summary>
    [ShaderBinding("vge_dbgRays", ShaderBindingKind.AtomicCounter, 0, ShaderStageKind.Compute, Required = false)]
    GpuAtomicCounterBuffer? DebugCounters { set; }
    #endregion
}
