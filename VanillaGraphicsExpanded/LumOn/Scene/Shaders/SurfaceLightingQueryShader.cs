using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares bounded GPU evaluation of CPU-produced geometry-hit descriptors.</summary>
[ShaderProgram("Contract", "lumonscene_surface_query", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_surface_query.csh")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
[ShaderBindingSet(typeof(SurfaceLightingBindingSet), Program = "Contract")]
[ShaderBindingSet(typeof(TraceGeometryBindingSet), Program = "Contract")]
internal static partial class SurfaceLightingQueryShader {

    #region Private: GPU binding declarations
    /// <summary>Declares the SurfaceQueries StorageBlock slot.</summary>
    [ShaderBinding("SurfaceQueries", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    private static partial ShaderStorageBlockBinding SurfaceQueries { get; }
    #endregion
 }
