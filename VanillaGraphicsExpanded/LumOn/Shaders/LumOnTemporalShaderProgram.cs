using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Temporal pass.
/// Blends current radiance with history for temporal stability.
/// </summary>
public class LumOnTemporalShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnTemporalShaderProgram
        {
            PassName = "lumon_temporal",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("lumon_temporal", instance);
        instance.Compile();
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

    #endregion

    #region Matrix Uniforms

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

    #endregion

    #region Temporal Uniforms

    /// <summary>
    /// Temporal blend factor (0.0-1.0). Higher = more history.
    /// </summary>
    public float TemporalAlpha { set => Uniform("temporalAlpha", value); }

    /// <summary>
    /// Depth threshold for history rejection.
    /// </summary>
    public float DepthRejectThreshold { set => Uniform("depthRejectThreshold", value); }

    /// <summary>
    /// Normal threshold for history rejection (dot product).
    /// </summary>
    public float NormalRejectThreshold { set => Uniform("normalRejectThreshold", value); }

    #endregion
}
