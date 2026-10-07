using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Displays an unmodified debug texture through the owned shader submission contract.</summary>
[ShaderProgram("Contract", "debug_texture", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "debug_texture.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "debug_texture.fsh")]
internal sealed partial class DebugTextureShaderProgram : GpuProgram, IDebugTextureBindings
{
    /// <inheritdoc />
    internal override GpuShaderContract ProgramContract => Contract;

    #region Public API
    /// <summary>Installs the generated fixed sampler contract.</summary>
    public DebugTextureShaderProgram() => ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);

    /// <summary>Supplies the displayed image using the shared nearest-clamp sampler.</summary>
    public partial int Scene { set; }
    #endregion
}
