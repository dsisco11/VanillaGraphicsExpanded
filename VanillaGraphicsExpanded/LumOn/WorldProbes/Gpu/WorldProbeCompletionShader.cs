using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Compacts unresolved descriptors separately from ready directional payloads.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_completion", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_completion.csh")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
internal static partial class WorldProbeCompletionShader { }
