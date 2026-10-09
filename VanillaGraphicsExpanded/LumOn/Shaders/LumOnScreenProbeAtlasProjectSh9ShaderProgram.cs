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
public partial class LumOnScreenProbeAtlasProjectSh9ShaderProgram : LumOnShaderProgram, ILumOnScreenProbeAtlasProjectSh9ShaderProgramBindings
{


    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

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
    public partial GpuTexture? ScreenProbeAtlas { set; }

    /// <summary>
    /// Input stabilized/filtered probe-atlas meta (confidence + flags).
    /// </summary>
    public partial GpuTexture? ScreenProbeAtlasMeta { set; }

    /// <summary>
    /// Probe anchor positions for validity checks.
    /// </summary>
    public partial GpuTexture? ProbeAnchors { set; }

    #endregion

    // Per-frame state (probeGridSize) is provided via LumOnFrameUBO.
    #region Binding sources
    /// <summary>Supplies current frame storage through the binding contract.</summary>
    /// <summary>Reuses the shared camera snapshot rather than the effect-specific lighting buffer.</summary>
    CpuUniformBuffer ILumOnScreenProbeAtlasProjectSh9ShaderProgramBindings.FrameInputs => SharedCamera;
    GpuUniformBuffer? ILumOnScreenProbeAtlasProjectSh9ShaderProgramBindings.LumOnFrame => RetainedFrame;
    /// <summary>Supplies retained world-probe storage when the installed variant consumes it.</summary>
    GpuUniformBuffer? ILumOnScreenProbeAtlasProjectSh9ShaderProgramBindings.LumOnWorldProbe => RetainedWorldProbe;
    #endregion
}
