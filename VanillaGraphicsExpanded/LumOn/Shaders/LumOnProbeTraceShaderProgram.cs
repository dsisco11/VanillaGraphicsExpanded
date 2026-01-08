using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Probe Trace pass.
/// Ray marches from each probe and accumulates radiance into SH coefficients.
/// </summary>
public class LumOnProbeTraceShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnProbeTraceShaderProgram
        {
            PassName = "lumon_probe_trace",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("lumon_probe_trace", instance);
        instance.Compile();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Probe anchor positions (posWS.xyz, valid) - stored in world-space for temporal stability.
    /// </summary>
    public int ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 0); }

    /// <summary>
    /// Probe anchor normals (normalWS.xyz, reserved) - stored in world-space for temporal stability.
    /// </summary>
    public int ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 1); }

    /// <summary>
    /// Primary depth texture for ray marching.
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 2); }

    /// <summary>
    /// Primary color texture (scene radiance source).
    /// </summary>
    public int PrimaryColor { set => BindTexture2D("primaryColor", value, 3); }

    #endregion

    #region Matrix Uniforms

    /// <summary>
    /// Inverse projection matrix for position reconstruction.
    /// </summary>
    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    /// <summary>
    /// Projection matrix for screen-space ray marching.
    /// </summary>
    public float[] ProjectionMatrix { set => UniformMatrix("projectionMatrix", value); }

    /// <summary>
    /// View matrix for world-space to view-space transformation.
    /// Probe anchors are stored in world-space, but ray marching requires view-space.
    /// </summary>
    public float[] ViewMatrix { set => UniformMatrix("viewMatrix", value); }

    #endregion

    #region Probe Grid Uniforms

    /// <summary>
    /// Spacing between probes in pixels.
    /// </summary>
    public int ProbeSpacing { set => Uniform("probeSpacing", value); }

    /// <summary>
    /// Probe grid dimensions (probeCountX, probeCountY).
    /// </summary>
    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    /// <summary>
    /// Screen dimensions in pixels.
    /// </summary>
    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    #endregion

    #region Ray Tracing Uniforms

    /// <summary>
    /// Frame index for temporal jittering.
    /// </summary>
    public int FrameIndex { set => Uniform("frameIndex", value); }

    /// <summary>
    /// Number of rays per probe per frame.
    /// </summary>
    public int RaysPerProbe { set => Uniform("raysPerProbe", value); }

    /// <summary>
    /// Number of steps per ray.
    /// </summary>
    public int RaySteps { set => Uniform("raySteps", value); }

    /// <summary>
    /// Maximum ray distance in world units.
    /// </summary>
    public float RayMaxDistance { set => Uniform("rayMaxDistance", value); }

    /// <summary>
    /// Ray thickness for depth comparison.
    /// </summary>
    public float RayThickness { set => Uniform("rayThickness", value); }

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

    #region Sky/Sun Uniforms

    /// <summary>
    /// Weight applied to sky miss samples.
    /// </summary>
    public float SkyMissWeight { set => Uniform("skyMissWeight", value); }

    /// <summary>
    /// Sun direction in world space.
    /// </summary>
    public Vec3f SunPosition { set => Uniform("sunPosition", value); }

    /// <summary>
    /// Sun color and intensity.
    /// </summary>
    public Vec3f SunColor { set => Uniform("sunColor", value); }

    /// <summary>
    /// Ambient sky color.
    /// </summary>
    public Vec3f AmbientColor { set => Uniform("ambientColor", value); }

    /// <summary>
    /// Tint applied to indirect bounced light.
    /// </summary>
    public Vec3f IndirectTint { set => Uniform("indirectTint", value); }

    #endregion
}
