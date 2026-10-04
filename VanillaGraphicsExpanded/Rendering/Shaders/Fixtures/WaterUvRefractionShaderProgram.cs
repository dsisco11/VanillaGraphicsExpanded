using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Executes the production UV receiver through the established precompiled fixture catalog.</summary>
[ShaderProgram("Contract", "tests/water_uv_refraction", 3)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/GpuFramebufferBlendStateIntegrationTests_1.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/water_uv_refraction.fsh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal sealed partial class WaterUvRefractionShaderProgram : GpuProgram, IWaterUvRefractionBindings
{
    #region Public API
    /// <summary>Loads the immutable fixture binary through the shared shader owner.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    #endregion
}
