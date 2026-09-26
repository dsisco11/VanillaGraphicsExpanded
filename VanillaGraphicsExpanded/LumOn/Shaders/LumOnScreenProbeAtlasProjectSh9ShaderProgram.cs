using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for projecting the screen-probe atlas to SH9 coefficients per probe.
/// Used for the "EvaluateProjectedSH" gather mode (Phase 12 Option B).
/// </summary>
[ShaderProgram("Contract", "lumon_probe_atlas_project_sh9", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_probe_atlas_project_sh9.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_probe_atlas_project_sh9.fsh")]
public partial class LumOnScreenProbeAtlasProjectSh9ShaderProgram : LumOnShaderProgram
{

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => Contract;

    public LumOnScreenProbeAtlasProjectSh9ShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasProjectSh9ShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Input stabilized/filtered probe atlas (RGB radiance, A hit distance).
    /// </summary>
    public GpuTexture? ScreenProbeAtlas { set => BindTexture2D("octahedralAtlas", value, 0); }

    /// <summary>
    /// Input stabilized/filtered probe-atlas meta (confidence + flags).
    /// </summary>
    public GpuTexture? ScreenProbeAtlasMeta { set => BindTexture2D("probeAtlasMeta", value, 1); }

    /// <summary>
    /// Probe anchor positions for validity checks.
    /// </summary>
    public GpuTexture? ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 2); }

    #endregion

    // Per-frame state (probeGridSize) is provided via LumOnFrameUBO.
}
