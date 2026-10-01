using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Resolves scene-linear opaque lighting into the engine's display-referred primary target.</summary>
[ShaderProgram("Contract", "pbr_display_resolve", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_composite.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_display_resolve.fsh")]
public sealed partial class PBRDisplayResolveShaderProgram : GpuProgram, IPBRDisplayResolveShaderProgramBindings
{

    #region Contract and resources

    /// <summary>Uses the immutable shader declaration.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    /// <summary>Registers the display resolve's explicit sampler bindings.</summary>
    public PBRDisplayResolveShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Scene-linear lighting, except for the legacy display-referred sky.</summary>
    public int PrimaryScene { set => BindExternalTexture2D("primaryScene", value, 0, GpuSamplers.NearestClamp); }

    /// <summary>Depth distinguishes geometry from the unchanged legacy sky.</summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 1, GpuSamplers.NearestClamp); }

    #endregion
}
