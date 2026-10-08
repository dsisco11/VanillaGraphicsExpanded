using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Displays an unmodified debug texture through the owned shader submission contract.</summary>
[ShaderProgram("Contract", "debug_surface", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "debug_texture.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "debug_surface.fsh")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(MaterialLayer))]
internal sealed partial class DebugSurfaceShaderProgram : GpuProgram, IDebugSurfaceBindings
{
    /// <inheritdoc />
    internal override GpuShaderContract ProgramContract => Contract;

    #region Public API
    /// <summary>Installs the generated fixed sampler contract.</summary>
    public DebugSurfaceShaderProgram() => ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);

    /// <summary>Supplies the displayed image using the shared nearest-clamp sampler.</summary>
    public partial VanillaGraphicsExpanded.Rendering.GpuTexture? Scene { set; }
    /// <summary>Selects material instead of normal array data for the overlay.</summary>
    [ShaderOption("VGE_DEBUG_MATERIAL", false)]
    public partial bool MaterialLayer { get; set; }
    #endregion
}
