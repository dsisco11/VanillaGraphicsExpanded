using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for SurfaceLightingBindingSet.</summary>
internal interface ISurfaceLightingBindingSet
{
    #region Public API
    /// <summary>Declares the SurfaceLightingParams UniformBlock slot.</summary>
    [ShaderBinding("SurfaceLightingParams", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Lights, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    ShaderUniformBlockBinding Parameters { get; }
    /// <summary>Declares the capturedMaterial Sampler slot.</summary>
    [ShaderBinding("capturedMaterial", ShaderBindingKind.Sampler, 16, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    ShaderSamplerBinding CapturedMaterial { get; }
    /// <summary>Declares the previousOutgoing Sampler slot.</summary>
    [ShaderBinding("previousOutgoing", ShaderBindingKind.Sampler, 17, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    ShaderSamplerBinding PreviousOutgoing { get; }
    /// <summary>Declares the surfacePages Sampler slot.</summary>
    [ShaderBinding("surfacePages", ShaderBindingKind.Sampler, 18, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    ShaderSamplerBinding SurfacePages { get; }
    /// <summary>Declares the SurfacePatches StorageBlock slot.</summary>
    [ShaderBinding("SurfacePatches", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    ShaderStorageBlockBinding SurfacePatches { get; }
    /// <summary>Declares the SurfaceSlots StorageBlock slot.</summary>
    [ShaderBinding("SurfaceSlots", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    ShaderStorageBlockBinding SurfaceSlots { get; }
    /// <summary>Declares the SurfaceReady StorageBlock slot.</summary>
    [ShaderBinding("SurfaceReady", ShaderBindingKind.StorageBlock, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    ShaderStorageBlockBinding SurfaceReady { get; }
    #endregion
}
