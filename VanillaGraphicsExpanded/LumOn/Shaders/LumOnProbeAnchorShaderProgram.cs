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
public partial class LumOnProbeAnchorShaderProgram : LumOnShaderProgram
{

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => Contract;

    private LumOnProbeParamsUbo? paramsUbo;

    public LumOnProbeAnchorShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    private LumOnProbeParamsUbo Params => paramsUbo ??= new LumOnProbeParamsUbo();

    #region Static

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
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 0, GpuSamplers.NearestClamp); }

    /// <summary>
    /// G-buffer world-space normals.
    /// </summary>
    public int GBufferNormal { set => BindExternalTexture2D("gBufferNormal", value, 1, GpuSamplers.NearestClamp); }

    /// <summary>
    /// PMJ jitter sequence texture (RG16_UNorm, width=cycleLength, height=1).
    /// </summary>
    public GpuTexture? PmjJitter { set => BindTexture2D("pmjJitter", value, 2); }

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
            Params.BindTo(this, LumOnProbeParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    #endregion
}
