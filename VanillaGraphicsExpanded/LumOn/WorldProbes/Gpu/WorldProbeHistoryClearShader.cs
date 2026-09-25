using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Declares the batched directional and scalar history invalidation compute program.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_history_clear", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_history_clear.csh")]
internal static partial class WorldProbeHistoryClearShader { }
