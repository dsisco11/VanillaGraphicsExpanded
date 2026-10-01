using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Declares bounded world-probe geometry traversal and optional Surface Cache lookup.</summary>
[ShaderProgram("Contract", "lumon_worldprobe_trace", 2)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumon_worldprobe_trace.csh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal sealed partial class WorldProbeTraceShader : VanillaGraphicsExpanded.LumOn.Scene.Shaders.TraceGeometryComputeShader, IWorldProbeTraceShaderBindings
{
    /// <summary>Adopts the executable and its retained resource contract.</summary>
    public WorldProbeTraceShader(GpuComputePipeline pipeline) : base(pipeline) { }
}
