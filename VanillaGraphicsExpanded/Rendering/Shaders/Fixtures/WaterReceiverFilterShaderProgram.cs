using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Reads shared receiver filtering through the established offline shader catalog.</summary>
[ShaderProgram("Contract", "tests/water_receiver_filter", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/GpuFramebufferBlendStateIntegrationTests_1.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/water_receiver_filter.fsh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal sealed partial class WaterReceiverFilterShaderProgram : GpuProgram, IWaterReceiverFilterBindings
{
    #region Public API
    /// <summary>Loads the declared immutable binary through the normal shader owner.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    #endregion
}
