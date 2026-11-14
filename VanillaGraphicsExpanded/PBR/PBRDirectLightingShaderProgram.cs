using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Evaluates deferred surface lighting with shared current light and virtual-shadow inputs.</summary>
[ShaderProgram("Contract", "pbr_direct_lighting", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_direct_lighting.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_direct_lighting.fsh")]
public sealed partial class PBRDirectLightingShaderProgram : GpuProgram, IPBRDirectLightingShaderProgramBindings
{
    private VgeFrameUniformBuffer? frameInputs;
    private VgeLightsUniformBuffer? lightsInputs;
    private Shadows.SurfaceShadowInputSet? shadowInputs;

    #region Public API
    /// <summary>Declares the independently prepared production SPIR-V program.</summary>
    public static void Register(ICoreClientAPI api)
    {
        GpuShaderProgramLibraries.Declare(api, Contract.Identity, () => new PBRDirectLightingShaderProgram
        { PassName = Contract.Identity, AssetDomain = Constants.ModId });
    }

    /// <summary>Supplies the deferred base-color texture.</summary>
    public partial GpuTextureRef PrimaryScene { set; }
    /// <summary>Supplies visibility depth for terrain and unbiased first-person reconstruction.</summary>
    public partial GpuTextureRef PrimaryDepth { set; }
    /// <summary>Supplies explicitly retained first-person view positions.</summary>
    public partial GpuTextureRef GBufferPosition { set; }
    /// <summary>Supplies packed normal, material and environment layers.</summary>
    public partial GpuTextureRef GBufferSurface { set; }

    /// <summary>Borrows a coherent alternate camera for independently owned views and fixtures.</summary>
    internal VgeFrameUniformBuffer? FrameInputs
    {
        get => frameInputs;
        set { RequireInputMutation(); frameInputs = value; }
    }

    /// <summary>Borrows the matching alternate illumination list without changing its ownership.</summary>
    internal VgeLightsUniformBuffer? LightsInputs
    {
        get => lightsInputs;
        set { RequireInputMutation(); lightsInputs = value; }
    }

    /// <summary>Borrows a matching light/shadow interval for an explicitly owned alternate view.</summary>
    internal Shadows.SurfaceShadowInputSet? ShadowInputs
    {
        get => shadowInputs;
        set { RequireInputMutation(); shadowInputs = value; }
    }

    /// <inheritdoc />
    internal override GpuShaderContract ProgramContract => Contract;

    /// <inheritdoc />
    protected override GpuProgramLayout CreateLayout()
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(GpuShaderContracts.Create(Contract.Identity));
        return layout;
    }

    /// <summary>Reuses the same camera block as surface reconstruction and other lighting passes.</summary>
    CpuUniformBuffer IPBRDirectLightingShaderProgramBindings.FrameInputs => frameInputs ?? VgeFrameRenderer.Current;
    /// <summary>Reuses the shared illumination list independently of shadow admission.</summary>
    CpuUniformBuffer IPBRDirectLightingShaderProgramBindings.LightsInputs => lightsInputs ?? VgeLightsRenderer.Current;
    /// <summary>Uses the optional exact alternate shadow interval or the current shared frame set.</summary>
    Shadows.SurfaceShadowInputSet? Shadows.ISurfaceShadowBindings.ShadowInputs => shadowInputs;
    #endregion
}
