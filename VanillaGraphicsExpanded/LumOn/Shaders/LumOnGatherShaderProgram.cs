using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Gather pass.
/// Interpolates nearby probes to compute per-pixel irradiance.
/// </summary>
public class LumOnGatherShaderProgram : VgeShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnGatherShaderProgram
        {
            PassName = "lumon_gather",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterMemoryShaderProgram("lumon_gather", instance);
        instance.Initialize(api);
        instance.CompileAndLink();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Radiance SH texture 0 (from temporal history).
    /// </summary>
    public int RadianceTexture0 { set => BindTexture2D("radianceTexture0", value, 0); }

    /// <summary>
    /// Radiance SH texture 1 (from temporal history).
    /// </summary>
    public int RadianceTexture1 { set => BindTexture2D("radianceTexture1", value, 1); }

    /// <summary>
    /// Probe anchor positions.
    /// </summary>
    public int ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 2); }

    /// <summary>
    /// Probe anchor normals.
    /// </summary>
    public int ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 3); }

    /// <summary>
    /// Primary depth texture.
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 4); }

    /// <summary>
    /// G-buffer normals.
    /// </summary>
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 5); }

    #endregion

    #region Matrix Uniforms

    /// <summary>
    /// Inverse projection matrix for position reconstruction.
    /// </summary>
    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    /// <summary>
    /// View matrix for WS to VS normal transform.
    /// SH coefficients are stored in view-space directions, so pixel normal must be transformed.
    /// </summary>
    public float[] ViewMatrix { set => UniformMatrix("viewMatrix", value); }

    #endregion

    #region Probe Grid Uniforms

    /// <summary>
    /// Spacing between probes in pixels.
    /// </summary>
    public int ProbeSpacing { set => Uniform("probeSpacing", value); }

    /// <summary>
    /// Probe grid dimensions.
    /// </summary>
    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    /// <summary>
    /// Full-resolution screen size.
    /// </summary>
    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    /// <summary>
    /// Half-resolution buffer size.
    /// </summary>
    public Vec2f HalfResSize { set => Uniform("halfResSize", value); }

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

    #region Quality Uniforms

    /// <summary>
    /// Depth discontinuity threshold for edge detection.
    /// </summary>
    public float DepthDiscontinuityThreshold { set => Uniform("depthDiscontinuityThreshold", value); }

    /// <summary>
    /// Intensity multiplier for indirect lighting.
    /// </summary>
    public float Intensity { set => Uniform("intensity", value); }

    /// <summary>
    /// RGB tint for indirect lighting.
    /// </summary>
    public float[] IndirectTint { set => Uniform("indirectTint", new Vec3f(value[0], value[1], value[2])); }

    /// <summary>
    /// Controls depth similarity falloff for edge-aware weighting.
    /// Higher values = more lenient depth matching.
    /// Default: 0.5 (from SPG-007 spec Section 2.3)
    /// </summary>
    public float DepthSigma { set => Uniform("depthSigma", value); }

    /// <summary>
    /// Controls normal similarity power for edge-aware weighting.
    /// Higher values = stricter normal matching.
    /// Default: 8.0 (from SPG-007 spec Section 2.3)
    /// </summary>
    public float NormalSigma { set => Uniform("normalSigma", value); }

    #endregion
}
