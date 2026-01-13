using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for projecting the screen-probe atlas to SH9 coefficients per probe.
/// Used for the "EvaluateProjectedSH" gather mode (Phase 12 Option B).
/// </summary>
public class LumOnScreenProbeAtlasProjectSh9ShaderProgram : VgeShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasProjectSh9ShaderProgram
        {
            PassName = "lumon_probe_atlas_project_sh9",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterMemoryShaderProgram("lumon_probe_atlas_project_sh9", instance);
        instance.Initialize(api);
        instance.CompileAndLink();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Input stabilized/filtered probe atlas (RGB radiance, A hit distance).
    /// </summary>
    public int ScreenProbeAtlas { set => BindTexture2D("octahedralAtlas", value, 0); }

    /// <summary>
    /// Input stabilized/filtered probe-atlas meta (confidence + flags).
    /// </summary>
    public int ScreenProbeAtlasMeta { set => BindTexture2D("probeAtlasMeta", value, 1); }

    /// <summary>
    /// Probe anchor positions for validity checks.
    /// </summary>
    public int ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 2); }

    #endregion

    #region Uniforms

    /// <summary>
    /// Probe grid dimensions (probeCountX, probeCountY).
    /// </summary>
    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    #endregion
}
