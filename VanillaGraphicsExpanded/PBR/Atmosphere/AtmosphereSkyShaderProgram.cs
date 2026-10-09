using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.ModSystems;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Owns the precompiled atmospheric fullscreen pass and its complete texture and input contract.</summary>
[ShaderProgram("Contract", "pbr_sky", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_sky.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_sky.fsh")]
internal sealed partial class AtmosphereSkyShaderProgram : GpuProgram, IAtmosphereSkyShaderProgramBindings
{
    private readonly AtmosphereSkyInputs inputs;
    private VgeFrameUniformBuffer? frameInputs;

    #region Public API
    /// <summary>Uses the offline sky declaration for demand loading and reload.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    /// <summary>Retains guarded frame inputs with the executable owner.</summary>
    public AtmosphereSkyShaderProgram()
    {
        inputs = OwnUniformBuffer(new AtmosphereSkyInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Samples the coherently published two-lobe atmosphere lookup.</summary>
    public partial int SkyLookup { set; }
    /// <summary>Samples engine liquid depth for shoreline and underwater masking.</summary>
    public partial int LiquidDepth { set; }

    /// <summary>Borrows a frame snapshot for alternate views and fixtures.</summary>
    internal VgeFrameUniformBuffer? FrameInputs
    {
        get => frameInputs;
        set { RequireInputMutation(); frameInputs = value; }
    }

    /// <summary>Stages compatibility effects before submission.</summary>
    internal void Capture(ICoreClientAPI api, AtmosphereLighting lighting, bool sceneLinear)
    {
        inputs.Capture(api, lighting, sceneLinear);
        SkyLookup = AtmosphereModSystem.SkyTextureId;
        LiquidDepth = api.Render.FrameBuffers[(int)EnumFrameBuffer.LiquidDepth].DepthTextureId;
    }

    /// <summary>Publishes the matching view-ray transforms and spatial effects in one block.</summary>
    CpuUniformBuffer IAtmosphereSkyShaderProgramBindings.Inputs => inputs;
    /// <summary>Shares the camera snapshot without per-sky matrix publication.</summary>
    CpuUniformBuffer IAtmosphereSkyShaderProgramBindings.FrameInputs => frameInputs ?? VgeFrameRenderer.Current;
    #endregion
}
