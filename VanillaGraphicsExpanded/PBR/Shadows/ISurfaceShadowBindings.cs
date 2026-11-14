using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Shadows;

/// <summary>Shares integer depth, selected rendered mappings and whole-light readiness across all surface executables.</summary>
internal interface ISurfaceShadowBindings
{
    #region Public API
    /// <summary>Allows an explicitly owned alternate view to borrow its exact light/readiness interval.</summary>
    SurfaceShadowInputSet? ShadowInputs { get; }
    /// <summary>Resolves the consumer's explicit set or the immutable current frame's shared set.</summary>
    SurfaceShadowInputSet SurfaceShadows => ShadowInputs ?? SurfaceShadowBindings.CurrentInputs;
    /// <summary>Current direct illumination and exact source indices remain available when shadows are disabled.</summary>
    [ShaderBinding("SurfaceShadowParameters",ShaderBindingKind.UniformBlock,18,ShaderStageKind.Fragment,Required=false)]
    CpuUniformBuffer SurfaceParameters=>SurfaceShadows.Parameters;
    /// <summary>Requests retain the projection selected by GPU readiness, including permitted stale mappings.</summary>
    [ShaderBinding("SurfaceShadowRequests",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Fragment,Required=false)]
    GpuShaderStorageBuffer SurfaceRequests=>SurfaceShadows.Requests;
    /// <summary>Completed assignments and coverage are shared with the readiness reducer.</summary>
    [ShaderBinding("PageEntries",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Fragment,Required=false)]
    GpuShaderStorageBuffer SurfaceEntries=>SurfaceShadows.Entries;
    /// <summary>Every surface uses one per-source GPU eligibility result.</summary>
    [ShaderBinding("SurfaceShadowReadiness",ShaderBindingKind.StorageBlock,2,ShaderStageKind.Fragment,Required=false)]
    GpuShaderStorageBuffer SurfaceReadiness=>SurfaceShadows.Readiness;
    /// <summary>Unexpected lookup failures are counted on GPU rather than downloading shadow images.</summary>
    [ShaderBinding("SurfaceShadowDiagnostics",ShaderBindingKind.StorageBlock,5,ShaderStageKind.Fragment,Required=false)]
    GpuShaderStorageBuffer SurfaceDiagnostics=>SurfaceShadows.Diagnostics;
    /// <summary>One integer texture array supplies every manually resolved filter tap.</summary>
    [ShaderBinding("shadowDepthPool",ShaderBindingKind.Sampler,33,ShaderStageKind.Fragment,ShaderType=ShaderResourceType.UnsignedIntSampler2DArray,Sampler=ShaderSamplerPolicy.NearestClamp,Required=false)]
    GpuTextureRef SurfaceDepth=>SurfaceShadows.Depth;
    #endregion
}
