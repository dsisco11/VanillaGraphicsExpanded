using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Publishes ready hybrid directions and their common metadata in one ordered compute commit.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_commit", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_commit.csh")]
internal static partial class WorldProbeCommitShader { }
