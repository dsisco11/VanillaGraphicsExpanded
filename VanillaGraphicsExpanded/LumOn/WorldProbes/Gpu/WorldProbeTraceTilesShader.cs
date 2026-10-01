using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Declares GPU generation of probe direction tiles and indirect trace arguments.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_trace_tiles", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_trace_tiles.csh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal sealed partial class WorldProbeTraceTilesShader : GpuComputeShader, IWorldProbeTraceTilesShaderBindings
{
    /// <summary>Adopts the executable and its retained resource contract.</summary>
    public WorldProbeTraceTilesShader(GpuComputePipeline pipeline) : base(pipeline) { }
}
