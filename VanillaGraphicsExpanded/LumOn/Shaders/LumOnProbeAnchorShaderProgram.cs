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
/// Shader program for LumOn Probe Anchor pass.
/// Determines probe positions from G-buffer depth/normals.
///
/// Output is in WORLD-SPACE (matching UE5 Lumen's design) for temporal stability:
/// - World-space directions remain valid across camera rotations
/// - Radiance stored per world-space direction can be directly blended
/// - No SH rotation or coordinate transforms needed in temporal pass
///
/// Implements validation criteria from LumOn.02-Probe-Grid.md:
/// - Sky rejection (depth >= 0.9999)
/// - Edge detection via depth discontinuity (reduces temporal weight)
/// - Invalid normal rejection
/// </summary>
[ShaderProgram("Contract", "lumon_probe_anchor", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_probe_anchor.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_probe_anchor.fsh")]
public partial class LumOnProbeAnchorShaderProgram : LumOnShaderProgram, ILumOnProbeAnchorShaderProgramBindings
{


    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    private LumOnProbeParamsUbo? paramsUbo;

    /// <summary>Registers the probe anchor resource contract.</summary>
    public LumOnProbeAnchorShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Retains packed parameters and rejects mutation during submission.</summary>
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

    /// <summary>Supplies shared frame storage through the existing uniform-block contract.</summary>
    /// <summary>Reuses the shared camera snapshot rather than the effect-specific lighting buffer.</summary>
    CpuUniformBuffer ILumOnProbeAnchorShaderProgramBindings.FrameInputs => SharedCamera;
    GpuUniformBuffer? ILumOnProbeAnchorShaderProgramBindings.LumOnFrame => RetainedFrame;
    /// <summary>Supplies packed parameters for one generated publication per use.</summary>
    CpuUniformBuffer ILumOnProbeAnchorShaderProgramBindings.Parameters => Params;

    #region Static

    /// <summary>Declares the persistent probe anchor owner.</summary>
    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnProbeAnchorShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Primary depth texture for position reconstruction.
    /// </summary>
    public partial int PrimaryDepth { set; }

    /// <summary>
    /// G-buffer world-space normals.
    /// </summary>
    public partial GpuTexture? GBufferSurface { set; }

    /// <summary>
    /// PMJ jitter sequence texture (RG16_UNorm, width=cycleLength, height=1).
    /// </summary>
    public partial GpuTexture? PmjJitter { set; }

    #endregion

    // Per-frame state (matrices, sizes, frame index, jitter config, zNear/zFar) is provided via LumOnFrameUBO.

    #region Edge Detection Uniforms

    /// <summary>
    /// Threshold for depth discontinuity detection.
    /// Probes at edges (depth discontinuities) are marked with partial validity (0.5)
    /// for reduced temporal accumulation weight.
    /// Recommended value: 0.1
    /// </summary>
    public float DepthDiscontinuityThreshold
    {
        set
        {
            Params.DepthDiscontinuityThreshold = value;
        }
    }

    #endregion
}
