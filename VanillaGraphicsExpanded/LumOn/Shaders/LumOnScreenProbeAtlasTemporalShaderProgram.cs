using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn screen-probe atlas temporal pass.
/// Implementation detail: operates on an octahedral-mapped direction atlas.
/// Performs per-texel temporal blending for the probe atlas.
/// Only blends texels traced this frame; preserves non-traced texels.
/// Uses hit-distance delta for disocclusion detection.
/// </summary>
public class LumOnScreenProbeAtlasTemporalShaderProgram : GpuProgram
{
    public LumOnScreenProbeAtlasTemporalShaderProgram()
    {
        RegisterUniformBlockBinding("LumOnFrameUBO", LumOnUniformBuffers.FrameBinding, required: true);
    }

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasTemporalShaderProgram
        {
            PassName = "lumon_probe_atlas_temporal",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("lumon_probe_atlas_temporal", instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Current frame probe atlas trace output.
    /// Shader uniform name remains <c>octahedralCurrent</c> for compatibility.
    /// </summary>
    public GpuTexture? ScreenProbeAtlasCurrent { set => BindTexture2D("octahedralCurrent", value, 0); }

    /// <summary>
    /// History probe atlas from previous frame.
    /// Shader uniform name remains <c>octahedralHistory</c> for compatibility.
    /// </summary>
    public GpuTexture? ScreenProbeAtlasHistory { set => BindTexture2D("octahedralHistory", value, 1); }

    /// <summary>
    /// Probe anchor positions for validity check.
    /// </summary>
    public GpuTexture? ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 2); }

    /// <summary>
    /// Current frame probe-atlas meta trace output.
    /// </summary>
    public GpuTexture? ScreenProbeAtlasMetaCurrent { set => BindTexture2D("probeAtlasMetaCurrent", value, 3); }

    /// <summary>
    /// Previous frame probe-atlas meta history (after last swap).
    /// Used for confidence-aware temporal blending.
    /// </summary>
    public GpuTexture? ScreenProbeAtlasMetaHistory { set => BindTexture2D("probeAtlasMetaHistory", value, 4); }

    /// <summary>
    /// Phase 14 velocity buffer (RGBA32F): RG = currUv - prevUv, A = packed flags.
    /// Used for velocity-based reprojection.
    /// </summary>
    public GpuTexture? VelocityTex { set => BindTexture2D("velocityTex", value, 5); }

    /// <summary>
    /// PMJ jitter sequence texture (RG16_UNorm, width=cycleLength, height=1).
    /// Used to reconstruct the same jittered probe UV as the probe-anchor pass.
    /// </summary>
    public GpuTexture? PmjJitter { set => BindTexture2D("pmjJitter", value, 6); }

    #endregion

    // Per-frame state (probe grid params, screen size, frame index, jitter config, velocity reprojection toggles)
    // is provided via LumOnFrameUBO.

    #region Temporal Distribution Defines

    /// <summary>
    /// Number of probe-atlas texels traced per probe per frame.
    /// With 64 total texels and 8 per frame, full coverage takes 8 frames.
    /// Compile-time define for temporal distribution.
    /// </summary>
    public int TexelsPerFrame { set => SetDefine(VgeShaderDefines.LumOnAtlasTexelsPerFrame, value.ToString(CultureInfo.InvariantCulture)); }

    #endregion

    #region Temporal Blending Uniforms

    /// <summary>
    /// Base temporal blend factor.
    /// Higher values = more history = more stable but slower response.
    /// E.g., 0.9 = 90% history, 10% current.
    /// </summary>
    public float TemporalAlpha { set => Uniform("temporalAlpha", value); }

    /// <summary>
    /// Hit-distance rejection threshold for disocclusion detection.
    /// Relative difference threshold (e.g., 0.3 = 30%).
    /// If hit distance changed more than this, reject history.
    /// </summary>
    public float HitDistanceRejectThreshold { set => Uniform("hitDistanceRejectThreshold", value); }

    #endregion
}
