using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn velocity generation pass.
/// Produces a per-pixel screen-space velocity (UV delta per frame) and packed reprojection flags.
/// </summary>
public class LumOnVelocityShaderProgram : GpuProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnVelocityShaderProgram
        {
            PassName = "lumon_velocity",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();

        api.Shader.RegisterMemoryShaderProgram("lumon_velocity", instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Primary depth texture (current frame).
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 0, GpuSamplers.NearestClamp); }

    #endregion

    // Per-frame state (screen size, invCurrViewProj, prevViewProj, historyValid) is provided via LumOnFrameUBO.
}
