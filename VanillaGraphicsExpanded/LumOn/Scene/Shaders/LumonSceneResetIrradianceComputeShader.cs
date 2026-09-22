using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "lumonscene_reset_irradiance", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_reset_irradiance.csh")]
internal static partial class LumonSceneResetIrradianceComputeShader
{
}
