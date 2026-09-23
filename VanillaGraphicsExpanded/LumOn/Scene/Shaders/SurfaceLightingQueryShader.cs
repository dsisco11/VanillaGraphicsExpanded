using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares bounded GPU evaluation of CPU-produced geometry-hit descriptors.</summary>
[ShaderProgram("Contract", "lumonscene_surface_query", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_surface_query.csh")]
internal static partial class SurfaceLightingQueryShader { }
