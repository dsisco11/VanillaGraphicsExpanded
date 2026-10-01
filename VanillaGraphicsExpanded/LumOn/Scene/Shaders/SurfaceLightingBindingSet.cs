using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns a shared compile-time GPU interface.</summary>
internal static partial class SurfaceLightingBindingSet
{
    #region Private
    /// <summary>Declares the SurfaceLightingParams UniformBlock slot.</summary>
    [ShaderBinding("SurfaceLightingParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Lights, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderUniformBlockBinding Parameters { get; }
    /// <summary>Declares the capturedMaterial Sampler slot.</summary>
    [ShaderBinding("capturedMaterial", ShaderBindingKind.Sampler, 16, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderSamplerBinding CapturedMaterial { get; }
    /// <summary>Declares the previousOutgoing Sampler slot.</summary>
    [ShaderBinding("previousOutgoing", ShaderBindingKind.Sampler, 17, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderSamplerBinding PreviousOutgoing { get; }
    /// <summary>Declares the surfacePages Sampler slot.</summary>
    [ShaderBinding("surfacePages", ShaderBindingKind.Sampler, 18, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderSamplerBinding SurfacePages { get; }
    /// <summary>Declares the SurfacePatches StorageBlock slot.</summary>
    [ShaderBinding("SurfacePatches", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderStorageBlockBinding SurfacePatches { get; }
    /// <summary>Declares the SurfaceSlots StorageBlock slot.</summary>
    [ShaderBinding("SurfaceSlots", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderStorageBlockBinding SurfaceSlots { get; }
    /// <summary>Declares the SurfaceReady StorageBlock slot.</summary>
    [ShaderBinding("SurfaceReady", ShaderBindingKind.StorageBlock, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderStorageBlockBinding SurfaceReady { get; }
    #endregion
}
