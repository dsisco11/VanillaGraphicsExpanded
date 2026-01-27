using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn screen-probe atlas filter pass.
/// Performs an edge-stopped denoise within each probe's octahedral tile.
/// </summary>
public class LumOnScreenProbeAtlasFilterShaderProgram : GpuProgram
{
    public LumOnScreenProbeAtlasFilterShaderProgram()
    {
        RegisterUniformBlockBinding("LumOnFrameUBO", LumOnUniformBuffers.FrameBinding, required: true);
    }

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasFilterShaderProgram
        {
            PassName = "lumon_probe_atlas_filter",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("lumon_probe_atlas_filter", instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Input stabilized screen-probe atlas radiance.
    /// Shader uniform name remains <c>octahedralAtlas</c> for compatibility.
    /// </summary>
    public GpuTexture? ScreenProbeAtlas { set => BindTexture2D("octahedralAtlas", value, 0); }

    /// <summary>
    /// Input stabilized probe-atlas meta (confidence + flags).
    /// </summary>
    public GpuTexture? ScreenProbeAtlasMeta { set => BindTexture2D("probeAtlasMeta", value, 1); }

    /// <summary>
    /// Probe anchor positions for validity checks.
    /// </summary>
    public GpuTexture? ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 2); }

    #endregion

    #region Uniforms
    // Per-frame state (probeGridSize) is provided via LumOnFrameUBO.

    /// <summary>
    /// Filter radius in texels (1 = 3x3).
    /// </summary>
    public int FilterRadius { set => Uniform("filterRadius", value); }

    /// <summary>
    /// Edge-stopping sigma for hit-distance differences (decoded distance units).
    /// </summary>
    public float HitDistanceSigma { set => Uniform("hitDistanceSigma", value); }

    #endregion
}
