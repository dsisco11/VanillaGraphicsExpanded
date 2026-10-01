using VanillaGraphicsExpanded.Rendering.Contracts;
using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn screen-probe atlas filter pass.
/// Performs an edge-stopped denoise within each probe's octahedral tile.
/// </summary>
[ShaderProgram("Contract", "lumon_probe_atlas_filter", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_probe_atlas_filter.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_probe_atlas_filter.fsh")]
public partial class LumOnScreenProbeAtlasFilterShaderProgram : LumOnShaderProgram, ILumOnScreenProbeAtlasFilterShaderProgramBindings
{


    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    private LumOnProbeParamsUbo? paramsUbo;

    public LumOnScreenProbeAtlasFilterShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Owns packed parameters and rejects writes during publication.</summary>
    private LumOnProbeParamsUbo Params
    {
        get
        {
            if (paramsUbo is null)
            {
                paramsUbo = new LumOnProbeParamsUbo();
                paramsUbo.SetWriteGuard(RequireInputMutation);
            }
            return paramsUbo;
        }
    }

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasFilterShaderProgram
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
    public partial GpuTexture? ProbeAnchorPosition { set; }

    #endregion

    #region Uniforms
    // Per-frame state (probeGridSize) is provided via LumOnFrameUBO.

    /// <summary>
    /// Filter radius in texels (1 = 3x3).
    /// </summary>
    public int FilterRadius
    {
        set
        {
            Params.FilterRadius = value;
        }
    }

    /// <summary>
    /// Edge-stopping sigma for hit-distance differences (decoded distance units).
    /// </summary>
    public float HitDistanceSigma
    {
        set
        {
            Params.HitDistanceSigma = value;
        }
    }

    #endregion
    #region Binding sources
    /// <summary>Supplies current frame storage through the binding contract.</summary>
    GpuUniformBuffer? ILumOnScreenProbeAtlasFilterShaderProgramBindings.LumOnFrame => RetainedFrame;
    /// <summary>Supplies retained world-probe storage when the installed variant consumes it.</summary>
    GpuUniformBuffer? ILumOnScreenProbeAtlasFilterShaderProgramBindings.LumOnWorldProbe => RetainedWorldProbe;
    /// <summary>Supplies the retained CPU block for one publication per use.</summary>
    CpuUniformBuffer ILumOnScreenProbeAtlasFilterShaderProgramBindings.Parameters => Params;
    #endregion
}
