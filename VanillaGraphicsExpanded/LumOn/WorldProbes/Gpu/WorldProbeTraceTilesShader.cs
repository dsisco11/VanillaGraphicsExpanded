using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Declares GPU generation of probe direction tiles and indirect trace arguments.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_trace_tiles", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_trace_tiles.csh")]
internal static partial class WorldProbeTraceTilesShader { }
