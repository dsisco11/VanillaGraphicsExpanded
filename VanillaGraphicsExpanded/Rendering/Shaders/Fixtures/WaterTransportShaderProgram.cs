using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares readback of production linear water composition and directional transport.</summary>
[ShaderProgram("Contract", "tests/water_transport", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/GpuFramebufferBlendStateIntegrationTests_1.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/water_transport.fsh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal sealed partial class WaterTransportShaderProgram : GpuProgram
{
    #region Public API
    /// <summary>Loads only the offline-built production-helper fixture.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    #endregion
}
