using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the bounded direct, indirect and outgoing surface-lighting producer.</summary>
[ShaderProgram("Contract", "lumonscene_surface_lighting", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_surface_lighting.csh")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
[ShaderBindingSet(typeof(SurfaceLightingBindingSet), Program = "Contract")]
[ShaderBindingSet(typeof(TraceGeometryBindingSet), Program = "Contract")]
internal static partial class LumonSceneSurfaceLightingShader {

    #region Private: GPU binding declarations
    /// <summary>Declares the lightColors Sampler slot.</summary>
    [ShaderBinding("lightColors", ShaderBindingKind.Sampler, 3, ShaderStageKind.Compute)]
    private static partial ShaderSamplerBinding LightColors { get; }
    /// <summary>Declares the blockLevels Sampler slot.</summary>
    [ShaderBinding("blockLevels", ShaderBindingKind.Sampler, 4, ShaderStageKind.Compute)]
    private static partial ShaderSamplerBinding BlockLevels { get; }
    /// <summary>Declares the sunLevels Sampler slot.</summary>
    [ShaderBinding("sunLevels", ShaderBindingKind.Sampler, 5, ShaderStageKind.Compute)]
    private static partial ShaderSamplerBinding SunLevels { get; }
    /// <summary>Declares the surfaces Sampler slot.</summary>
    [ShaderBinding("surfaces", ShaderBindingKind.Sampler, 7, ShaderStageKind.Compute)]
    private static partial ShaderSamplerBinding Surfaces { get; }
    /// <summary>Declares the indirectIrradiance Image slot.</summary>
    [ShaderBinding("indirectIrradiance", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    private static partial ShaderImageBinding IndirectIrradiance { get; }
    /// <summary>Declares the directIrradiance Image slot.</summary>
    [ShaderBinding("directIrradiance", ShaderBindingKind.Image, 1, ShaderStageKind.Compute)]
    private static partial ShaderImageBinding DirectIrradiance { get; }
    /// <summary>Declares the nextOutgoing Image slot.</summary>
    [ShaderBinding("nextOutgoing", ShaderBindingKind.Image, 2, ShaderStageKind.Compute)]
    private static partial ShaderImageBinding NextOutgoing { get; }
    /// <summary>Declares the SurfaceWork StorageBlock slot.</summary>
    [ShaderBinding("SurfaceWork", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    private static partial ShaderStorageBlockBinding SurfaceWork { get; }
    /// <summary>Declares the SurfaceFallbackRequests StorageBlock slot.</summary>
    [ShaderBinding("SurfaceFallbackRequests", ShaderBindingKind.StorageBlock, 5, ShaderStageKind.Compute)]
    private static partial ShaderStorageBlockBinding SurfaceFallbackRequests { get; }
    /// <summary>Declares the SurfaceFallbackCommits StorageBlock slot.</summary>
    [ShaderBinding("SurfaceFallbackCommits", ShaderBindingKind.StorageBlock, 6, ShaderStageKind.Compute)]
    private static partial ShaderStorageBlockBinding SurfaceFallbackCommits { get; }
    /// <summary>Declares the SurfaceHitRetries StorageBlock slot.</summary>
    [ShaderBinding("SurfaceHitRetries", ShaderBindingKind.StorageBlock, 7, ShaderStageKind.Compute)]
    private static partial ShaderStorageBlockBinding SurfaceHitRetries { get; }
    #endregion
 }
