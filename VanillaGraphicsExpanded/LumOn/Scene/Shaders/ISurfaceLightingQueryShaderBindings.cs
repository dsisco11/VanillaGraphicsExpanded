using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for SurfaceLightingQueryShader.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ISurfaceLightingQueryShaderBindings : ISurfaceLightingInputs
{
    #region Public API
    /// <summary>Declares the SurfaceQueries StorageBlock slot.</summary>
    [ShaderBinding("SurfaceQueries", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuStorageBufferBinding SurfaceQueries { set; }
    #endregion
}
