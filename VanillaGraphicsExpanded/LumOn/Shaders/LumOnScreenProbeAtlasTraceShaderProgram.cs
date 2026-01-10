using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn screen-probe atlas trace pass.
/// Implementation detail: uses an octahedral-mapped direction atlas.
/// Ray traces from each probe and stores radiance + hit distance in the probe atlas.
/// Uses temporal distribution to trace a subset of directions each frame.
/// </summary>
public class LumOnScreenProbeAtlasTraceShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasTraceShaderProgram
        {
            PassName = "lumon_probe_atlas_trace",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("lumon_probe_atlas_trace", instance);
        instance.Compile();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Probe anchor positions (posWS.xyz, valid) - stored in world-space.
    /// </summary>
    public int ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 0); }

    /// <summary>
    /// Probe anchor normals (normalWS.xyz, reserved) - stored in world-space.
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

    /// <summary>
    /// History probe atlas (octahedral-mapped) for temporal preservation.
    /// Shader uniform name remains <c>octahedralHistory</c> for compatibility.
    /// </summary>
    public int ScreenProbeAtlasHistory { set => BindTexture2D("octahedralHistory", value, 4); }

    /// <summary>
    /// Optional HZB depth pyramid (mipmapped R32F).
    /// </summary>
    public int HzbDepth { set => BindTexture2D("hzbDepth", value, 5); }

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
    /// </summary>
    public float[] ViewMatrix { set => UniformMatrix("viewMatrix", value); }

    /// <summary>
    /// Inverse view matrix for view-space to world-space transformation.
    /// </summary>
    public float[] InvViewMatrix { set => UniformMatrix("invViewMatrix", value); }

    #endregion

    #region Probe Grid Uniforms

    /// <summary>
    /// Probe grid dimensions (probeCountX, probeCountY).
    /// </summary>
    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    /// <summary>
    /// Screen dimensions in pixels.
    /// </summary>
    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    #endregion

    #region Temporal Distribution Uniforms

    /// <summary>
    /// Current frame index for temporal distribution.
    /// </summary>
    public int FrameIndex { set => Uniform("frameIndex", value); }

    /// <summary>
    /// Number of probe-atlas texels to trace per frame (default 8).
    /// With 64 texels total, this means full coverage in 8 frames.
    /// </summary>
    public int TexelsPerFrame { set => Uniform("texelsPerFrame", value); }

    #endregion

    #region Ray Tracing Uniforms

    /// <summary>
    /// Number of ray march steps.
    /// </summary>
    public int RaySteps { set => Uniform("raySteps", value); }

    /// <summary>
    /// Maximum ray march distance in view-space units.
    /// </summary>
    public float RayMaxDistance { set => Uniform("rayMaxDistance", value); }

    /// <summary>
    /// Thickness threshold for depth test during ray marching.
    /// </summary>
    public float RayThickness { set => Uniform("rayThickness", value); }

    /// <summary>
    /// Coarse mip used for early rejection.
    /// </summary>
    public int HzbCoarseMip { set => Uniform("hzbCoarseMip", value); }

    #endregion

    #region Z-Planes

    /// <summary>
    /// Near clip plane distance.
    /// </summary>
    public float ZNear { set => Uniform("zNear", value); }

    /// <summary>
    /// Far clip plane distance.
    /// </summary>
    public float ZFar { set => Uniform("zFar", value); }

    #endregion

    #region Sky Fallback Uniforms

    /// <summary>
    /// Weight for sky color when ray misses (0 = black, 1 = full sky).
    /// </summary>
    public float SkyMissWeight { set => Uniform("skyMissWeight", value); }

    /// <summary>
    /// Normalized sun direction for sky fallback.
    /// </summary>
    public Vec3f SunPosition { set => Uniform("sunPosition", value); }

    /// <summary>
    /// Sun color for sky fallback.
    /// </summary>
    public Vec3f SunColor { set => Uniform("sunColor", value); }

    /// <summary>
    /// Ambient color for sky fallback.
    /// </summary>
    public Vec3f AmbientColor { set => Uniform("ambientColor", value); }

    #endregion

    #region Indirect Lighting

    /// <summary>
    /// Tint color applied to indirect lighting.
    /// </summary>
    public Vec3f IndirectTint { set => Uniform("indirectTint", value); }

    #endregion
}
