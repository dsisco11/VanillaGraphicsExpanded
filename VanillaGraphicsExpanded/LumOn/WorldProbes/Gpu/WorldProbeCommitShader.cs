using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Publishes ready hybrid directions and their common metadata in one ordered compute commit.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_commit", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_commit.csh")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
internal static partial class WorldProbeCommitShader { }
