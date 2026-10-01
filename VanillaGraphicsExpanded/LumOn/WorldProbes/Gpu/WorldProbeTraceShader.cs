using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Declares bounded world-probe geometry traversal and optional Surface Cache lookup.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_trace", 2)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_trace.csh")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
internal static partial class WorldProbeTraceShader { }
