using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Declares the batched directional and scalar history invalidation compute program.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_history_clear", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_history_clear.csh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal sealed partial class WorldProbeHistoryClearShader : GpuComputeShader, IWorldProbeHistoryClearShaderBindings
{
    /// <summary>Adopts the executable and its retained resource contract.</summary>
    public WorldProbeHistoryClearShader(GpuComputePipeline pipeline) : base(pipeline) { }
}
