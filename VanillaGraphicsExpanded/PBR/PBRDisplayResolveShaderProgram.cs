using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Hands off opaque lighting and optional particles using the selected scene color convention.</summary>
[ShaderProgram("Contract", "pbr_display_resolve", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_composite.vsh", Identity = "pbr_display_resolve.vsh")]
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
    public partial int PrimaryScene { set; }

    /// <summary>Depth distinguishes geometry from the unchanged legacy sky.</summary>
    public partial int PrimaryDepth { set; }

    /// <summary>Preserves scene radiance when the complete HDR producer/consumer handoff is ready.</summary>
    public partial int SceneLinear { set; }

    /// <summary>Composes ordered particle radiance over transported opaque lighting on the linear route.</summary>
    public partial DynamicTexture2D? ParticleLayer { set; }

    /// <summary>Enables sampling only for a completed particle layer from the current frame.</summary>
    public partial int ParticleLayerEnabled { set; }

    #endregion
    #region Binding sources
    #endregion
}
