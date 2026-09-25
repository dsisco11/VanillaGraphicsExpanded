using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Declares bounded world-probe geometry traversal and optional Surface Cache lookup.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_trace", 2)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_trace.csh")]
internal static partial class WorldProbeTraceShader { }
