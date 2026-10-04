using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Exposes production receiver diagnostics through the existing offline shader catalog.</summary>
[ShaderProgram("Contract", "tests/water_refraction_diagnostics", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/GpuFramebufferBlendStateIntegrationTests_1.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/water_refraction_diagnostics.fsh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal sealed partial class WaterRefractionDiagnosticShaderProgram : GpuProgram, IWaterRefractionDiagnosticBindings
{
    #region Public API
    /// <summary>Loads the immutable diagnostic binary through the shared program owner.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    #endregion
}
