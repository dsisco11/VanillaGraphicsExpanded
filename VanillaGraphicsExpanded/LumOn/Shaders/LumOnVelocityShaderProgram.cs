using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn velocity generation pass.
/// Produces a per-pixel screen-space velocity (UV delta per frame) and packed reprojection flags.
/// </summary>
[ShaderProgram("Contract", "lumon_velocity", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_velocity.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_velocity.fsh")]
public partial class LumOnVelocityShaderProgram : LumOnShaderProgram
{

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => Contract;

    public LumOnVelocityShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnVelocityShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Primary depth texture (current frame).
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 0, GpuSamplers.NearestClamp); }

    #endregion

    // Per-frame state (screen size, invCurrViewProj, prevViewProj, historyValid) is provided via LumOnFrameUBO.
}
