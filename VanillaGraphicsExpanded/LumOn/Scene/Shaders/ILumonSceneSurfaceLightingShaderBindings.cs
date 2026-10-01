using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for LumonSceneSurfaceLightingShader.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumonSceneSurfaceLightingShaderBindings : ISurfaceLightingInputs
{
    #region Public API
    /// <summary>Declares the lightColors Sampler slot.</summary>
    [ShaderBinding("lightColors", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? LightColors { set; }
    /// <summary>Declares the blockLevels Sampler slot.</summary>
    [ShaderBinding("blockLevels", ShaderBindingKind.Sampler, 4, ShaderStageKind.Compute, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? BlockLevels { set; }
    /// <summary>Declares the sunLevels Sampler slot.</summary>
    [ShaderBinding("sunLevels", ShaderBindingKind.Sampler, 5, ShaderStageKind.Compute, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? SunLevels { set; }
    /// <summary>Declares the surfaces Sampler slot.</summary>
    [ShaderBinding("surfaces", ShaderBindingKind.Sampler, 7, ShaderStageKind.Compute, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Surfaces { set; }
    /// <summary>Declares the indirectIrradiance Image slot.</summary>
    [ShaderBinding("indirectIrradiance", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    GpuTextureBinding IndirectIrradiance { set; }
    /// <summary>Declares the directIrradiance Image slot.</summary>
    [ShaderBinding("directIrradiance", ShaderBindingKind.Image, 1, ShaderStageKind.Compute)]
    GpuTextureBinding DirectIrradiance { set; }
    /// <summary>Declares the nextOutgoing Image slot.</summary>
    [ShaderBinding("nextOutgoing", ShaderBindingKind.Image, 2, ShaderStageKind.Compute)]
    GpuTextureBinding NextOutgoing { set; }
    /// <summary>Declares the SurfaceWork StorageBlock slot.</summary>
    [ShaderBinding("SurfaceWork", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? SurfaceWork { set; }
    /// <summary>Declares the SurfaceFallbackRequests StorageBlock slot.</summary>
    [ShaderBinding("SurfaceFallbackRequests", ShaderBindingKind.StorageBlock, 5, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? SurfaceFallbackRequests { set; }
    /// <summary>Declares the SurfaceFallbackCommits StorageBlock slot.</summary>
    [ShaderBinding("SurfaceFallbackCommits", ShaderBindingKind.StorageBlock, 6, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? SurfaceFallbackCommits { set; }
    /// <summary>Declares the SurfaceHitRetries StorageBlock slot.</summary>
    [ShaderBinding("SurfaceHitRetries", ShaderBindingKind.StorageBlock, 7, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? SurfaceHitRetries { set; }
    /// <summary>Retains only the diagnostics storage admitted for this dispatch.</summary>
    [ShaderBinding("SurfaceDiagnostics", ShaderBindingKind.StorageBlock, 4, ShaderStageKind.Compute, Required = false)]
    GpuShaderStorageBuffer? DiagnosticCounters { set; }
    #endregion
}
