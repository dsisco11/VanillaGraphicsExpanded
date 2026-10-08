using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn screen-probe atlas temporal pass.
/// Implementation detail: operates on an octahedral-mapped direction atlas.
/// Performs per-texel temporal blending for the probe atlas.
/// Only blends texels traced this frame; preserves non-traced texels.
/// Uses hit-distance delta for disocclusion detection.
/// </summary>
[ShaderProgram("Contract", "lumon_probe_atlas_temporal", 4)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_probe_atlas_temporal.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_probe_atlas_temporal.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Pis")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "AtlasUpdate")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(TexelsPerFrame), SpecializationId = 1)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(BatchSlicing))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(ImportanceSampling))]
public partial class LumOnScreenProbeAtlasTemporalShaderProgram : LumOnShaderProgram, ILumOnScreenProbeAtlasTemporalShaderProgramBindings
{


    #region Shader options
    /// <summary>Gets or sets the declared BatchSlicing shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.BatchSlicing))]
    public partial bool BatchSlicing { get; set; }

    /// <summary>Gets or sets the declared ImportanceSampling shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ImportanceSampling))]
    public partial bool ImportanceSampling { get; set; }
    #endregion

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    private LumOnProbeParamsUbo? paramsUbo;

    public LumOnScreenProbeAtlasTemporalShaderProgram()
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
        var instance = new LumOnScreenProbeAtlasTemporalShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    #region Product Importance Sampling Defines (Phase 10)

    /// <summary>Updates the importance-sampling switches consumed by this pass.</summary>
    public bool EnsureProbePisDefines(
        bool enabled,
        bool forceBatchSlicing)
    {
        bool changed = SetShaderOptions(options =>
        {
            options.Set(LumOnShaderOptions.ImportanceSampling, enabled);
            options.Set(LumOnShaderOptions.BatchSlicing, forceBatchSlicing);
        });
        return !changed;
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Current frame probe atlas trace output.
    /// Shader uniform name remains <c>octahedralCurrent</c> for compatibility.
    /// </summary>
    public partial GpuTexture? ScreenProbeAtlasCurrent { set; }

    /// <summary>
    /// History probe atlas from previous frame.
    /// Shader uniform name remains <c>octahedralHistory</c> for compatibility.
    /// </summary>
    public partial GpuTexture? ScreenProbeAtlasHistory { set; }

    /// <summary>
    /// Probe anchor positions for validity check.
    /// </summary>
    public partial GpuTexture? ProbeAnchors { set; }

    /// <summary>
    /// Current frame probe-atlas meta trace output.
    /// </summary>
    public partial GpuTexture? ScreenProbeAtlasMetaCurrent { set; }

        /// <summary>
        /// Probe-resolution trace mask (RG32F packed uint bits) selecting which atlas texels were traced.
        /// </summary>
        public partial GpuTexture? ProbeTraceMask { set; }

    /// <summary>
    /// Previous frame probe-atlas meta history (after last swap).
    /// Used for confidence-aware temporal blending.
    /// </summary>
    public partial GpuTexture? ScreenProbeAtlasMetaHistory { set; }

    /// <summary>
    /// Phase 14 velocity buffer (RGBA32F): RG = currUv - prevUv, A = packed flags.
    /// Used for velocity-based reprojection.
    /// </summary>
    public partial GpuTexture? VelocityTex { set; }

    /// <summary>
    /// PMJ jitter sequence texture (RG16_UNorm, width=cycleLength, height=1).
    /// Used to reconstruct the same jittered probe UV as the probe-anchor pass.
    /// </summary>
    public partial GpuTexture? PmjJitter { set; }

    #endregion

    // Per-frame state (probe grid params, screen size, frame index, jitter config, velocity reprojection toggles)
    // is provided via LumOnFrameUBO.

    #region Temporal Distribution Defines

    /// <summary>
    /// Number of probe-atlas texels traced per probe per frame.
    /// With 64 total texels and 8 per frame, full coverage takes 8 frames.
    /// Compile-time define for temporal distribution.
    /// </summary>
    /// <summary>Gets or sets the declared AtlasTexelsPerFrame shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.AtlasTexelsPerFrame))]
    public partial int TexelsPerFrame { get; set; }

    #endregion

    #region Temporal Blending Uniforms

    /// <summary>
    /// Base temporal blend factor.
    /// Higher values = more history = more stable but slower response.
    /// E.g., 0.9 = 90% history, 10% current.
    /// </summary>
    public float TemporalAlpha
    {
        set
        {
            Params.TemporalAlpha = value;
        }
    }

    /// <summary>
    /// Hit-distance rejection threshold for disocclusion detection.
    /// Relative difference threshold (e.g., 0.3 = 30%).
    /// If hit distance changed more than this, reject history.
    /// </summary>
    public float HitDistanceRejectThreshold
    {
        set
        {
            Params.HitDistanceRejectThreshold = value;
        }
    }

    #endregion
    #region Binding sources
    /// <summary>Supplies current frame storage through the binding contract.</summary>
    GpuUniformBuffer? ILumOnScreenProbeAtlasTemporalShaderProgramBindings.LumOnFrame => RetainedFrame;
    /// <summary>Supplies retained world-probe storage when the installed variant consumes it.</summary>
    GpuUniformBuffer? ILumOnScreenProbeAtlasTemporalShaderProgramBindings.LumOnWorldProbe => RetainedWorldProbe;
    /// <summary>Supplies the retained CPU block for one publication per use.</summary>
    CpuUniformBuffer ILumOnScreenProbeAtlasTemporalShaderProgramBindings.Parameters => Params;
    #endregion
}
