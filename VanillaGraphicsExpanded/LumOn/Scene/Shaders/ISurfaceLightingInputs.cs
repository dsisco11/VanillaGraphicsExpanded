using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for SurfaceLightingBindingSet.</summary>
internal interface ISurfaceLightingInputs
{
    #region Public API
    /// <summary>Declares the SurfaceLightingParams UniformBlock slot.</summary>
    [ShaderBinding("SurfaceLightingParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Lights, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    CpuUniformBuffer SurfaceLightingParameters { set; }
    /// <summary>Declares the capturedMaterial Sampler slot.</summary>
    [ShaderBinding("capturedMaterial", ShaderBindingKind.Sampler, 16, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? CapturedMaterial { set; }
    /// <summary>Declares the previousOutgoing Sampler slot.</summary>
    [ShaderBinding("previousOutgoing", ShaderBindingKind.Sampler, 17, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? PreviousOutgoing { set; }
    /// <summary>Declares the surfacePages Sampler slot.</summary>
    [ShaderBinding("surfacePages", ShaderBindingKind.Sampler, 18, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false, TextureTarget = ShaderTextureTarget.Texture2DArray, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? SurfacePages { set; }
    /// <summary>Declares the SurfacePatches StorageBlock slot.</summary>
    [ShaderBinding("SurfacePatches", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    GpuShaderStorageBuffer? SurfacePatches { set; }
    /// <summary>Declares the SurfaceSlots StorageBlock slot.</summary>
    [ShaderBinding("SurfaceSlots", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    GpuShaderStorageBuffer? SurfaceSlots { set; }
    /// <summary>Declares the SurfaceReady StorageBlock slot.</summary>
    [ShaderBinding("SurfaceReady", ShaderBindingKind.StorageBlock, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    GpuShaderStorageBuffer? SurfaceReady { set; }
    #endregion
}
