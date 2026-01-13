using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Temporal pass.
/// Blends current radiance with history for temporal stability.
/// Implements reprojection, validation, and neighborhood clamping.
/// See: LumOn.05-Temporal.md
/// </summary>
public class LumOnTemporalShaderProgram : VgeShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnTemporalShaderProgram
        {
            PassName = "lumon_temporal",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("lumon_temporal", instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Current frame radiance SH texture 0.
    /// </summary>
    public int RadianceCurrent0 { set => BindTexture2D("radianceCurrent0", value, 0); }

    /// <summary>
    /// Current frame radiance SH texture 1.
    /// </summary>
    public int RadianceCurrent1 { set => BindTexture2D("radianceCurrent1", value, 1); }

    /// <summary>
    /// History radiance SH texture 0.
    /// </summary>
    public int RadianceHistory0 { set => BindTexture2D("radianceHistory0", value, 2); }

    /// <summary>
    /// History radiance SH texture 1.
    /// </summary>
    public int RadianceHistory1 { set => BindTexture2D("radianceHistory1", value, 3); }

    /// <summary>
    /// Probe anchor positions for validation.
    /// </summary>
    public int ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 4); }

    /// <summary>
    /// Probe anchor normals for validation.
    /// </summary>
    public int ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 5); }

    /// <summary>
    /// History metadata texture (depth, normal, accumCount).
    /// </summary>
    public int HistoryMeta { set => BindTexture2D("historyMeta", value, 6); }

    /// <summary>
    /// Full-resolution velocity texture (RGBA32F): RG = velocityUv, A = packed flags.
    /// </summary>
    public int VelocityTex { set => BindTexture2D("velocityTex", value, 7); }

    #endregion

    #region Matrix Uniforms

    /// <summary>
    /// Current frame's view matrix (world-space to view-space).
    /// Needed for converting WS probe positions to VS for depth calculations.
    /// </summary>
    public float[] ViewMatrix { set => UniformMatrix("viewMatrix", value); }

    /// <summary>
    /// Current frame's inverse view matrix (view-space to world-space).
    /// </summary>
    public float[] InvViewMatrix { set => UniformMatrix("invViewMatrix", value); }

    /// <summary>
    /// Previous frame's view-projection matrix for reprojection.
    /// </summary>
    public float[] PrevViewProjMatrix { set => UniformMatrix("prevViewProjMatrix", value); }

    #endregion

    #region Probe Grid Uniforms

    /// <summary>
    /// Probe grid dimensions (probeCountX, probeCountY).
    /// </summary>
    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    /// <summary>
    /// Full-resolution screen size.
    /// Used to map probe grid coords to screen UV for velocity sampling.
    /// </summary>
    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    /// <summary>
    /// Probe spacing in pixels.
    /// Must match the probe anchor pass.
    /// </summary>
    public int ProbeSpacing { set => Uniform("probeSpacing", value); }

    /// <summary>
    /// Current frame index (used for deterministic anchor jitter replication).
    /// </summary>
    public int FrameIndex { set => Uniform("frameIndex", value); }

    /// <summary>
    /// Enables deterministic anchor jitter replication (0/1).
    /// Must match the probe anchor pass.
    /// </summary>
    public int AnchorJitterEnabled { set => Uniform("anchorJitterEnabled", value); }

    /// <summary>
    /// Anchor jitter amount as a fraction of probe cell size.
    /// Must match the probe anchor pass.
    /// </summary>
    public float AnchorJitterScale { set => Uniform("anchorJitterScale", value); }

    #endregion

    #region Depth Uniforms

    /// <summary>
    /// Near clip plane distance.
    /// </summary>
    public float ZNear { set => Uniform("zNear", value); }

    /// <summary>
    /// Far clip plane distance.
    /// </summary>
    public float ZFar { set => Uniform("zFar", value); }

    #endregion

    #region Temporal Uniforms

    /// <summary>
    /// Temporal blend factor (0.0-1.0). Higher = more history.
    /// </summary>
    public float TemporalAlpha { set => Uniform("temporalAlpha", value); }

    /// <summary>
    /// Depth threshold for history rejection (relative).
    /// </summary>
    public float DepthRejectThreshold { set => Uniform("depthRejectThreshold", value); }

    /// <summary>
    /// Normal threshold for history rejection (dot product).
    /// </summary>
    public float NormalRejectThreshold { set => Uniform("normalRejectThreshold", value); }

    /// <summary>
    /// Enables velocity-based reprojection path (0/1).
    /// </summary>
    public int EnableReprojectionVelocity { set => Uniform("enableReprojectionVelocity", value); }

    /// <summary>
    /// Reject/down-weight history when |velocityUv| exceeds this threshold.
    /// Units are UV delta per frame.
    /// </summary>
    public float VelocityRejectThreshold { set => Uniform("velocityRejectThreshold", value); }

    #endregion
}
