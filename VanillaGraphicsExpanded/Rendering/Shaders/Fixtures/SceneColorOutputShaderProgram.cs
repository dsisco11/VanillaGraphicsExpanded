using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Executes the production scene color helper from an offline-built numerical fixture.</summary>
[ShaderProgram("Contract", "tests/scene_color_output", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/GpuFramebufferBlendStateIntegrationTests_1.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/scene_color_output.fsh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal sealed partial class SceneColorOutputShaderProgram : GpuProgram
{
    #region Public API
    /// <summary>Loads the declared immutable binary fixture through the shared program owner.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    #endregion
}
