using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
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
    public LumOnScreenProbeAtlasTraceShaderProgram()
    {
        RegisterUniformBlockBinding("LumOnFrameUBO", LumOnUniformBuffers.FrameBinding, required: true);
        RegisterUniformBlockBinding("LumOnWorldProbeUBO", LumOnUniformBuffers.WorldProbeBinding, required: false);
    }

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
    public GpuTexture? ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 0); }

    /// <summary>
    /// Probe anchor normals (normalWS.xyz, reserved) - stored in world-space.
    /// </summary>
    public GpuTexture? ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 1); }

    /// <summary>
    /// Primary depth texture for ray marching.
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 2, GpuSamplers.NearestClamp); }

    /// <summary>
    /// Direct diffuse lighting (linear, pre-tonemap HDR).
    /// </summary>
    public GpuTexture? DirectDiffuse { set => BindTexture2D("directDiffuse", value, 3); }

    /// <summary>
    /// Emissive radiance (linear, pre-tonemap HDR).
    /// </summary>
    public GpuTexture? Emissive { set => BindTexture2D("emissive", value, 4); }

    /// <summary>
    /// History probe atlas (octahedral-mapped) for temporal preservation.
    /// Shader uniform name remains <c>octahedralHistory</c> for compatibility.
    /// </summary>
    public GpuTexture? ScreenProbeAtlasHistory { set => BindTexture2D("octahedralHistory", value, 5); }

    /// <summary>
    /// History probe-atlas meta (confidence + flags) for temporal preservation.
    /// </summary>
    public GpuTexture? ScreenProbeAtlasMetaHistory { set => BindTexture2D("probeAtlasMetaHistory", value, 7); }

    /// <summary>
    /// Optional HZB depth pyramid (mipmapped R32F).
    /// </summary>
    public GpuTexture? HzbDepth { set => BindTexture2D("hzbDepth", value, 6); }

    #endregion

    // Per-frame state (matrices, screen size, probe grid size, frame index, zNear/zFar, sun/ambient colors)
    // is provided via LumOnFrameUBO.

    #region Temporal Distribution Defines

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

    #region Sky Fallback Uniforms

    /// <summary>
    /// Weight for sky color when ray misses (0 = black, 1 = full sky).
    /// Compile-time define for sky contribution.
    /// </summary>
    public float SkyMissWeight { set => SetDefine(VgeShaderDefines.LumOnSkyMissWeight, value.ToString(CultureInfo.InvariantCulture)); }

    #endregion

    #region Indirect Lighting

    /// <summary>
    /// Tint color applied to indirect lighting.
    /// </summary>
    public Vec3f IndirectTint { set => Uniform("indirectTint", value); }

    #endregion

    #region World Probes (Phase 18)

    public bool EnsureWorldProbeClipmapDefines(bool enabled, float baseSpacing, int levels, int resolution, int worldProbeOctahedralTileSize)
    {
        if (!enabled)
        {
            baseSpacing = 0;
            levels = 0;
            resolution = 0;
            worldProbeOctahedralTileSize = 0;
        }

        bool changed = false;
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeEnabled, enabled ? "1" : "0");
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, levels.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, resolution.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, baseSpacing.ToString("0.0####", CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeOctahedralSize, worldProbeOctahedralTileSize.ToString(CultureInfo.InvariantCulture));
        return !changed;
    }

    public GpuTexture? WorldProbeSH0 { set => BindTexture2D("worldProbeSH0", value, 8); }
    public GpuTexture? WorldProbeSH1 { set => BindTexture2D("worldProbeSH1", value, 9); }
    public GpuTexture? WorldProbeSH2 { set => BindTexture2D("worldProbeSH2", value, 10); }
    public GpuTexture? WorldProbeVis0 { set => BindTexture2D("worldProbeVis0", value, 11); }
    public GpuTexture? WorldProbeMeta0 { set => BindTexture2D("worldProbeMeta0", value, 12); }
    public GpuTexture? WorldProbeSky0 { set => BindTexture2D("worldProbeSky0", value, 13); }

    #endregion
}
