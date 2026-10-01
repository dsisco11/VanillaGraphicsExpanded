using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Compacts unresolved descriptors separately from ready directional payloads.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_completion", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_completion.csh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal static partial class WorldProbeCompletionShader { }
