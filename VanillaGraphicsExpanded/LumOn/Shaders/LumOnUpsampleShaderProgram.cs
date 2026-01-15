using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Upsample pass.
/// Bilateral upsamples half-res indirect diffuse to full resolution.
/// </summary>
public class LumOnUpsampleShaderProgram : VgeShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnUpsampleShaderProgram
        {
            PassName = "lumon_upsample",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("lumon_upsample", instance);
    }

    #endregion

    #region Matrix Uniforms

    /// <summary>
    /// Inverse projection matrix for view-space position reconstruction.
    /// </summary>
    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    /// <summary>
    /// View matrix for WS to VS normal transform.
    /// </summary>
    public float[] ViewMatrix { set => UniformMatrix("viewMatrix", value); }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Half-resolution indirect diffuse texture.
    /// </summary>
    public int IndirectHalf { set => BindTexture2D("indirectHalf", value, 0); }

    /// <summary>
    /// Primary depth texture for edge-aware upsampling.
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 1); }

    /// <summary>
    /// G-buffer normals for edge-aware upsampling.
    /// </summary>
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 2); }

    #endregion

    #region Size Uniforms

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

    #region Quality Uniforms / Defines

    /// <summary>
    /// Whether edge-aware denoising is enabled.
    /// Compile-time define for better performance.
    /// </summary>
    public bool DenoiseEnabled { set => SetDefine(VgeShaderDefines.LumOnUpsampleDenoise, value ? "1" : "0"); }

    /// <summary>
    /// Depth similarity sigma for bilateral upsample.
    /// Controls how strictly depth differences affect upsampling.
    /// Default: 0.1 (from SPG-008 spec Section 3.1)
    /// </summary>
    public float UpsampleDepthSigma { set => Uniform("upsampleDepthSigma", value); }

    /// <summary>
    /// Normal similarity power for bilateral upsample.
    /// Controls how strictly normal differences affect upsampling.
    /// Default: 16.0 (from SPG-008 spec Section 3.1)
    /// </summary>
    public float UpsampleNormalSigma { set => Uniform("upsampleNormalSigma", value); }

    /// <summary>
    /// Spatial kernel sigma for optional spatial denoise.
    /// Controls blur radius of spatial filter.
    /// Default: 1.0 (from SPG-008 spec Section 3.1)
    /// </summary>
    public float UpsampleSpatialSigma { set => Uniform("upsampleSpatialSigma", value); }

    #endregion

    #region Hole Fill Uniforms / Defines

    /// <summary>
    /// Whether low-confidence hole filling is enabled.
    /// Compile-time define for better performance.
    /// </summary>
    public bool HoleFillEnabled { set => SetDefine(VgeShaderDefines.LumOnUpsampleHoleFill, value ? "1" : "0"); }

    /// <summary>
    /// Neighborhood radius in half-res pixels used for hole filling.
    /// Kept as uniform since it controls loop iteration bounds at runtime.
    /// </summary>
    public int HoleFillRadius { set => Uniform("holeFillRadius", value); }

    /// <summary>
    /// Minimum confidence (alpha) required for a neighbor sample to contribute to hole filling.
    /// </summary>
    public float HoleFillMinConfidence { set => Uniform("holeFillMinConfidence", value); }

    #endregion
}
