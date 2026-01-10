using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn screen-probe atlas gather pass.
/// Implementation detail: integrates radiance from an octahedral-mapped probe atlas.
/// This replaces the SH-based gather when UseProbeAtlas is enabled.
/// </summary>
public class LumOnScreenProbeAtlasGatherShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasGatherShaderProgram
        {
            PassName = "lumon_probe_atlas_gather",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("lumon_probe_atlas_gather", instance);
        instance.Compile();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Screen-probe atlas radiance texture.
    /// Shader uniform name remains <c>octahedralAtlas</c> for compatibility.
    /// Layout: (probeCountX × 8, probeCountY × 8)
    /// Format: RGB = radiance, A = log-encoded hit distance
    /// </summary>
    public int ScreenProbeAtlas { set => BindTexture2D("octahedralAtlas", value, 0); }

    /// <summary>
    /// Probe anchor positions (world-space).
    /// Format: xyz = posWS, w = validity
    /// </summary>
    public int ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 1); }

    /// <summary>
    /// Probe anchor normals (world-space, encoded).
    /// </summary>
    public int ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 2); }

    /// <summary>
    /// Primary depth texture (G-buffer).
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 3); }

    /// <summary>
    /// G-buffer normals (world-space, encoded).
    /// </summary>
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 4); }

    #endregion

    #region Matrix Uniforms

    /// <summary>
    /// Inverse projection matrix for position reconstruction.
    /// </summary>
    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    /// <summary>
    /// View matrix for world-space to view-space transforms.
    /// Used for computing view-space depth for probe weighting.
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
    /// Full-resolution screen size in pixels.
    /// </summary>
    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    /// <summary>
    /// Half-resolution buffer size in pixels.
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
    /// Intensity multiplier for indirect lighting output.
    /// </summary>
    public float Intensity { set => Uniform("intensity", value); }

    /// <summary>
    /// RGB tint applied to indirect lighting.
    /// </summary>
    public float[] IndirectTint { set => Uniform("indirectTint", new Vec3f(value[0], value[1], value[2])); }

    /// <summary>
    /// Leak prevention threshold.
    /// If probe hit distance exceeds pixel depth × (1 + threshold),
    /// the contribution is reduced to prevent light leaking.
    /// Default: 0.5 (50% tolerance)
    /// </summary>
    public float LeakThreshold { set => Uniform("leakThreshold", value); }

    /// <summary>
    /// Sample stride for hemisphere integration.
    /// 1 = full quality (64 samples per probe)
    /// 2 = performance mode (16 samples per probe)
    /// </summary>
    public int SampleStride { set => Uniform("sampleStride", value); }

    #endregion
}
