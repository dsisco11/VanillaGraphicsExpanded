using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn screen-probe atlas trace pass.
/// Implementation detail: uses an octahedral-mapped direction atlas.
/// Ray traces from each probe and stores radiance + hit distance in the probe atlas.
/// Uses temporal distribution to trace a subset of directions each frame.
/// </summary>
public class LumOnScreenProbeAtlasTraceShaderProgram : GpuProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasTraceShaderProgram
        {
            PassName = "lumon_probe_atlas_trace",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("lumon_probe_atlas_trace", instance);
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
    /// Direct diffuse lighting (linear, pre-tonemap HDR).
    /// </summary>
    public int DirectDiffuse { set => BindTexture2D("directDiffuse", value, 3); }

    /// <summary>
    /// Emissive radiance (linear, pre-tonemap HDR).
    /// </summary>
    public int Emissive { set => BindTexture2D("emissive", value, 4); }

    /// <summary>
    /// History probe atlas (octahedral-mapped) for temporal preservation.
    /// Shader uniform name remains <c>octahedralHistory</c> for compatibility.
    /// </summary>
    public int ScreenProbeAtlasHistory { set => BindTexture2D("octahedralHistory", value, 5); }

    /// <summary>
    /// History probe-atlas meta (confidence + flags) for temporal preservation.
    /// </summary>
    public int ScreenProbeAtlasMetaHistory { set => BindTexture2D("probeAtlasMetaHistory", value, 7); }

    /// <summary>
    /// Optional HZB depth pyramid (mipmapped R32F).
    /// </summary>
    public int HzbDepth { set => BindTexture2D("hzbDepth", value, 6); }

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

    #region Temporal Distribution Defines

    /// <summary>
    /// Current frame index for temporal distribution.
    /// </summary>
    public int FrameIndex { set => Uniform("frameIndex", value); }

    /// <summary>
    /// Number of probe-atlas texels to trace per frame (default 8).
    /// With 64 texels total, this means full coverage in 8 frames.
    /// Compile-time define for temporal distribution.
    /// </summary>
    public int TexelsPerFrame { set => SetDefine(VgeShaderDefines.LumOnAtlasTexelsPerFrame, value.ToString(CultureInfo.InvariantCulture)); }

    #endregion

    #region Ray Tracing Defines

    /// <summary>
    /// Number of ray march steps.
    /// Compile-time define for loop bounds.
    /// </summary>
    public int RaySteps { set => SetDefine(VgeShaderDefines.LumOnRaySteps, value.ToString(CultureInfo.InvariantCulture)); }

    /// <summary>
    /// Maximum ray march distance in view-space units.
    /// Compile-time define for trace distance.
    /// </summary>
    public float RayMaxDistance { set => SetDefine(VgeShaderDefines.LumOnRayMaxDistance, value.ToString(CultureInfo.InvariantCulture)); }

    /// <summary>
    /// Thickness threshold for depth test during ray marching.
    /// Compile-time define for hit threshold.
    /// </summary>
    public float RayThickness { set => SetDefine(VgeShaderDefines.LumOnRayThickness, value.ToString(CultureInfo.InvariantCulture)); }

    /// <summary>
    /// Coarse mip used for early rejection.
    /// Compile-time define for HZB mip selection.
    /// </summary>
    public int HzbCoarseMip { set => SetDefine(VgeShaderDefines.LumOnHzbCoarseMip, value.ToString(CultureInfo.InvariantCulture)); }

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
    /// Compile-time define for sky contribution.
    /// </summary>
    public float SkyMissWeight { set => SetDefine(VgeShaderDefines.LumOnSkyMissWeight, value.ToString(CultureInfo.InvariantCulture)); }

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
