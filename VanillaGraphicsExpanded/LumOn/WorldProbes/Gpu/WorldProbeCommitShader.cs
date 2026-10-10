using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Publishes ready hybrid directions and their common metadata in one ordered compute commit.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_commit", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_commit.csh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal sealed partial class WorldProbeCommitShader : GpuComputeProgram, IWorldProbeCommitShaderBindings
{
    /// <summary>Adopts the executable and its retained resource contract.</summary>
    public WorldProbeCommitShader(GpuComputePipeline pipeline) : base(pipeline) { }
}
