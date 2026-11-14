using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Existing fullscreen vertex ABI and read-only whole-light diagnostic storage.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations),Defaults=true)]
internal interface IShadowReadinessDebugBindings
{
    /// <summary>GPU readiness records; one fixed 32-word record for every configured light slot.</summary>
    [ShaderBinding("ShadowReadinessResults",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Fragment)]
    GpuShaderStorageBuffer? Status { set; }
    /// <summary>GPU traversal, overflow, unresolved and continuation state for every bounded association slot.</summary>
    [ShaderBinding("ShadowCandidateHeaders",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Fragment)]
    GpuShaderStorageBuffer? Candidates { set; }
    /// <summary>Physical residency, assignment generation, invalidation and receiving coverage per page slot.</summary>
    [ShaderBinding("PageEntries",ShaderBindingKind.StorageBlock,2,ShaderStageKind.Fragment)]
    GpuShaderStorageBuffer? Pages { set; }
    /// <summary>Per-light unexpected fragment lookup failures, retained and visualized entirely on GPU.</summary>
    [ShaderBinding("SurfaceShadowDiagnostics",ShaderBindingKind.StorageBlock,3,ShaderStageKind.Fragment,Required=false)]
    GpuShaderStorageBuffer? SampleFailures { set; }
}
