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
    private readonly DisplayResolveUniformBuffer routing;
    #region Contract and resources

    /// <summary>Uses the immutable shader declaration.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    /// <summary>Registers the display resolve's explicit sampler bindings.</summary>
    public PBRDisplayResolveShaderProgram()
    {
        routing = OwnUniformBuffer(new DisplayResolveUniformBuffer());
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Scene-linear lighting, except for the legacy display-referred sky.</summary>
    public partial int PrimaryScene { set; }

    /// <summary>Depth distinguishes geometry from the unchanged legacy sky.</summary>
    public partial int PrimaryDepth { set; }

    /// <summary>Preserves scene radiance when the complete HDR producer/consumer handoff is ready.</summary>
    public int SceneLinear { set => routing.SceneLinear = value; }

    /// <summary>Composes ordered particle radiance over transported opaque lighting on the linear route.</summary>
    public partial DynamicTexture2D? ParticleLayer { set; }

    /// <summary>Enables sampling only for a completed particle layer from the current frame.</summary>
    public int ParticleLayerEnabled { set => routing.ParticleLayerEnabled = value; }

    #endregion
    #region Binding sources
    /// <summary>Publishes both controls together through the prepared uniform-block binding.</summary>
    CpuUniformBuffer IPBRDisplayResolveShaderProgramBindings.Routing => routing;
    #endregion
}
