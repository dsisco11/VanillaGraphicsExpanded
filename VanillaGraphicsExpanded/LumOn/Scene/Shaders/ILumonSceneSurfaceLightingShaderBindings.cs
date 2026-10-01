using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for LumonSceneSurfaceLightingShader.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
[ShaderBindingSet(typeof(ISurfaceLightingBindingSet), Program = "Contract")]
[ShaderBindingSet(typeof(ITraceGeometryBindingSet), Program = "Contract")]
internal interface ILumonSceneSurfaceLightingShaderBindings
{
    #region Public API
    /// <summary>Declares the lightColors Sampler slot.</summary>
    [ShaderBinding("lightColors", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)]
    ShaderSamplerBinding LightColors { get; }
    /// <summary>Declares the blockLevels Sampler slot.</summary>
    [ShaderBinding("blockLevels", ShaderBindingKind.Sampler, 4, ShaderStageKind.Compute)]
    ShaderSamplerBinding BlockLevels { get; }
    /// <summary>Declares the sunLevels Sampler slot.</summary>
    [ShaderBinding("sunLevels", ShaderBindingKind.Sampler, 5, ShaderStageKind.Compute)]
    ShaderSamplerBinding SunLevels { get; }
    /// <summary>Declares the surfaces Sampler slot.</summary>
    [ShaderBinding("surfaces", ShaderBindingKind.Sampler, 7, ShaderStageKind.Compute)]
    ShaderSamplerBinding Surfaces { get; }
    /// <summary>Declares the indirectIrradiance Image slot.</summary>
    [ShaderBinding("indirectIrradiance", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    ShaderImageBinding IndirectIrradiance { get; }
    /// <summary>Declares the directIrradiance Image slot.</summary>
    [ShaderBinding("directIrradiance", ShaderBindingKind.Image, 1, ShaderStageKind.Compute)]
    ShaderImageBinding DirectIrradiance { get; }
    /// <summary>Declares the nextOutgoing Image slot.</summary>
    [ShaderBinding("nextOutgoing", ShaderBindingKind.Image, 2, ShaderStageKind.Compute)]
    ShaderImageBinding NextOutgoing { get; }
    /// <summary>Declares the SurfaceWork StorageBlock slot.</summary>
    [ShaderBinding("SurfaceWork", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    ShaderStorageBlockBinding SurfaceWork { get; }
    /// <summary>Declares the SurfaceFallbackRequests StorageBlock slot.</summary>
    [ShaderBinding("SurfaceFallbackRequests", ShaderBindingKind.StorageBlock, 5, ShaderStageKind.Compute)]
    ShaderStorageBlockBinding SurfaceFallbackRequests { get; }
    /// <summary>Declares the SurfaceFallbackCommits StorageBlock slot.</summary>
    [ShaderBinding("SurfaceFallbackCommits", ShaderBindingKind.StorageBlock, 6, ShaderStageKind.Compute)]
    ShaderStorageBlockBinding SurfaceFallbackCommits { get; }
    /// <summary>Declares the SurfaceHitRetries StorageBlock slot.</summary>
    [ShaderBinding("SurfaceHitRetries", ShaderBindingKind.StorageBlock, 7, ShaderStageKind.Compute)]
    ShaderStorageBlockBinding SurfaceHitRetries { get; }
    #endregion
}
