using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the bounded direct, indirect and outgoing surface-lighting producer.</summary>
[ShaderProgram("Contract", "lumonscene_surface_lighting", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_surface_lighting.csh")]
internal static partial class LumonSceneSurfaceLightingShader { }
