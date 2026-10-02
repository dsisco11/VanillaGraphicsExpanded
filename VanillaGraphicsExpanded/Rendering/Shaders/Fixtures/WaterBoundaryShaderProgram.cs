using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares analytic readback of the production signed-boundary transport function.</summary>
[ShaderProgram("Contract", "tests/water_boundaries", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/GpuFramebufferBlendStateIntegrationTests_1.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/water_boundaries.fsh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal sealed partial class WaterBoundaryShaderProgram : GpuProgram
{
    /// <summary>Loads only the immutable offline-built fixture contract.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
}
