using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn velocity generation pass.
/// Produces a per-pixel screen-space velocity (UV delta per frame) and packed reprojection flags.
/// </summary>
public class LumOnVelocityShaderProgram : VgeShaderProgram
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
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 0); }

    #endregion

    #region Size Uniforms

    /// <summary>
    /// Full-resolution screen size.
    /// </summary>
    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    #endregion

    #region Matrix Uniforms

    /// <summary>
    /// Inverse current view-projection matrix.
    /// </summary>
    public float[] InvCurrViewProjMatrix { set => UniformMatrix("invCurrViewProjMatrix", value); }

    /// <summary>
    /// Previous frame view-projection matrix.
    /// </summary>
    public float[] PrevViewProjMatrix { set => UniformMatrix("prevViewProjMatrix", value); }

    #endregion

    #region History Validity

    /// <summary>
    /// Whether temporal history is valid (0/1).
    /// </summary>
    public int HistoryValid { set => Uniform("historyValid", value); }

    #endregion
}
