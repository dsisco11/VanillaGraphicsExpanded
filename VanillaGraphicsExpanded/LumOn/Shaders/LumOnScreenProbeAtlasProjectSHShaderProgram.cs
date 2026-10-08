using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for projecting the screen-probe atlas into packed SH L1 coefficients.
/// Used for Phase 12 Option B (cheap gather).
/// </summary>
[ShaderProgram("Contract", "lumon_probe_atlas_project_sh", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_probe_atlas_project_sh.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_probe_atlas_project_sh.fsh")]
public partial class LumOnScreenProbeAtlasProjectSHShaderProgram : LumOnShaderProgram, ILumOnScreenProbeAtlasProjectSHShaderProgramBindings
{


    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    public LumOnScreenProbeAtlasProjectSHShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasProjectSHShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Input stabilized screen-probe atlas radiance.
    /// Shader uniform name remains <c>octahedralAtlas</c> for compatibility.
    /// </summary>
    public partial GpuTexture? ScreenProbeAtlas { set; }

    /// <summary>
    /// Input stabilized probe-atlas meta (confidence + flags).
    /// </summary>
    public partial GpuTexture? ScreenProbeAtlasMeta { set; }

    /// <summary>
    /// Probe anchor positions for validity checks.
    /// </summary>
    public partial GpuTexture? ProbeAnchors { set; }

    #endregion

    // Per-frame state (viewMatrix, probeGridSize) is provided via LumOnFrameUBO.
    #region Binding sources
    /// <summary>Supplies current frame storage through the binding contract.</summary>
    GpuUniformBuffer? ILumOnScreenProbeAtlasProjectSHShaderProgramBindings.LumOnFrame => RetainedFrame;
    /// <summary>Supplies retained world-probe storage when the installed variant consumes it.</summary>
    GpuUniformBuffer? ILumOnScreenProbeAtlasProjectSHShaderProgramBindings.LumOnWorldProbe => RetainedWorldProbe;
    #endregion
}
