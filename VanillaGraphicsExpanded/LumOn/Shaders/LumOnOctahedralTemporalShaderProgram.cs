using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Octahedral Temporal pass.
/// Performs per-texel temporal blending for octahedral radiance cache.
/// Only blends texels traced this frame; preserves non-traced texels.
/// Uses hit-distance delta for disocclusion detection.
/// </summary>
public class LumOnOctahedralTemporalShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnOctahedralTemporalShaderProgram
        {
            PassName = "lumon_temporal_octahedral",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("lumon_temporal_octahedral", instance);
        instance.Compile();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Current frame octahedral trace output.
    /// Contains fresh data for traced texels, history copies for non-traced.
    /// </summary>
    public int OctahedralCurrent { set => BindTexture2D("octahedralCurrent", value, 0); }

    /// <summary>
    /// History octahedral atlas from previous frame.
    /// </summary>
    public int OctahedralHistory { set => BindTexture2D("octahedralHistory", value, 1); }

    /// <summary>
    /// Probe anchor positions for validity check.
    /// </summary>
    public int ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 2); }

    #endregion

    #region Probe Grid Uniforms

    /// <summary>
    /// Probe grid dimensions (probeCountX, probeCountY).
    /// </summary>
    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    #endregion

    #region Temporal Distribution Uniforms

    /// <summary>
    /// Current frame index for temporal distribution calculation.
    /// </summary>
    public int FrameIndex { set => Uniform("frameIndex", value); }

    /// <summary>
    /// Number of octahedral texels traced per probe per frame.
    /// With 64 total texels and 8 per frame, full coverage takes 8 frames.
    /// </summary>
    public int TexelsPerFrame { set => Uniform("texelsPerFrame", value); }

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
