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

    #region Probe Grid Uniforms

    /// <summary>
    /// Probe grid dimensions (probeCountX, probeCountY).
    /// </summary>
    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    /// <summary>
    /// Spacing between probes in pixels.
    /// </summary>
    public int ProbeSpacing { set => Uniform("probeSpacing", value); }

    /// <summary>
    /// Screen dimensions in pixels.
    /// </summary>
    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    /// <summary>
    /// Toggle deterministic probe anchor jitter (must match probe-anchor pass).
    /// </summary>
    public int AnchorJitterEnabled { set => Uniform("anchorJitterEnabled", value); }

    /// <summary>
    /// Jitter scale as a fraction of probe spacing (must match probe-anchor pass).
    /// </summary>
    public float AnchorJitterScale { set => Uniform("anchorJitterScale", value); }

    /// <summary>
    /// PMJ jitter cycle length (texture width).
    /// </summary>
    public int PmjCycleLength { set => Uniform("pmjCycleLength", value); }

    #endregion

    #region Temporal Distribution Defines

    /// <summary>
    /// Current frame index for temporal distribution calculation.
    /// </summary>
    public int FrameIndex { set => Uniform("frameIndex", value); }

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

    /// <summary>
    /// Enables velocity-based reprojection (0/1).
    /// </summary>
    public int EnableVelocityReprojection { set => Uniform("enableVelocityReprojection", value); }

    /// <summary>
    /// Reject/down-weight history when velocity magnitude exceeds this threshold (UV delta per frame).
    /// </summary>
    public float VelocityRejectThreshold { set => Uniform("velocityRejectThreshold", value); }

    #endregion
}
