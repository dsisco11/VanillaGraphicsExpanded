using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn debug visualization overlay.
/// Renders probe grid, depth, normals, and other debug views.
/// </summary>
public class LumOnDebugShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnDebugShaderProgram
        {
            PassName = "lumon_debug",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("lumon_debug", instance);
        instance.Compile();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Primary depth texture.
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 0); }

    /// <summary>
    /// G-buffer normals texture.
    /// </summary>
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 1); }

    /// <summary>
    /// Probe anchor positions (posVS.xyz, valid).
    /// </summary>
    public int ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 2); }

    /// <summary>
    /// Probe anchor normals.
    /// </summary>
    public int ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 3); }

    /// <summary>
    /// Radiance SH texture 0 (for SH debug view).
    /// </summary>
    public int RadianceTexture0 { set => BindTexture2D("radianceTexture0", value, 4); }

    /// <summary>
    /// Radiance SH texture 1 (for SH debug view - second texture for full unpacking).
    /// </summary>
    public int RadianceTexture1 { set => BindTexture2D("radianceTexture1", value, 5); }

    /// <summary>
    /// Half-resolution indirect diffuse.
    /// </summary>
    public int IndirectHalf { set => BindTexture2D("indirectHalf", value, 6); }

    /// <summary>
    /// History metadata texture (depth, normal, accumCount) for temporal debug.
    /// </summary>
    public int HistoryMeta { set => BindTexture2D("historyMeta", value, 7); }

    #endregion

    #region Size Uniforms

    /// <summary>
    /// Full-resolution screen size.
    /// </summary>
    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    /// <summary>
    /// Probe grid dimensions (probeCountX, probeCountY).
    /// </summary>
    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    /// <summary>
    /// Spacing between probes in pixels.
    /// </summary>
    public int ProbeSpacing { set => Uniform("probeSpacing", value); }

    #endregion

    #region Z-Plane Uniforms

    /// <summary>
    /// Near clipping plane distance.
    /// </summary>
    public float ZNear { set => Uniform("zNear", value); }

    /// <summary>
    /// Far clipping plane distance.
    /// </summary>
    public float ZFar { set => Uniform("zFar", value); }

    #endregion

    #region Matrix Uniforms

    /// <summary>
    /// Inverse projection matrix for position reconstruction.
    /// </summary>
    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    /// <summary>
    /// Inverse view matrix for temporal reprojection.
    /// </summary>
    public float[] InvViewMatrix { set => UniformMatrix("invViewMatrix", value); }

    /// <summary>
    /// Previous frame view-projection matrix for temporal reprojection.
    /// </summary>
    public float[] PrevViewProjMatrix { set => UniformMatrix("prevViewProjMatrix", value); }

    #endregion

    #region Temporal Config Uniforms

    /// <summary>
    /// Temporal blend factor.
    /// </summary>
    public float TemporalAlpha { set => Uniform("temporalAlpha", value); }

    /// <summary>
    /// Depth rejection threshold.
    /// </summary>
    public float DepthRejectThreshold { set => Uniform("depthRejectThreshold", value); }

    /// <summary>
    /// Normal rejection threshold (dot product).
    /// </summary>
    public float NormalRejectThreshold { set => Uniform("normalRejectThreshold", value); }

    #endregion

    #region Debug Uniforms

    /// <summary>
    /// Debug visualization mode.
    /// </summary>
    public int DebugMode { set => Uniform("debugMode", value); }

    #endregion
}
