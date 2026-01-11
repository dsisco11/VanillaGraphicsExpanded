using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Upsample pass.
/// Bilateral upsamples half-res indirect diffuse to full resolution.
/// </summary>
public class LumOnUpsampleShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnUpsampleShaderProgram
        {
            PassName = "lumon_upsample",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("lumon_upsample", instance);
        instance.Compile();
    }

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

    #region Quality Uniforms

    /// <summary>
    /// Whether edge-aware denoising is enabled (0 or 1).
    /// </summary>
    public int DenoiseEnabled { set => Uniform("denoiseEnabled", value); }

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

    #region Hole Fill Uniforms

    /// <summary>
    /// Whether low-confidence hole filling is enabled (0 or 1).
    /// </summary>
    public int HoleFillEnabled { set => Uniform("holeFillEnabled", value); }

    /// <summary>
    /// Neighborhood radius in half-res pixels used for hole filling.
    /// </summary>
    public int HoleFillRadius { set => Uniform("holeFillRadius", value); }

    /// <summary>
    /// Minimum confidence (alpha) required for a neighbor sample to contribute to hole filling.
    /// </summary>
    public float HoleFillMinConfidence { set => Uniform("holeFillMinConfidence", value); }

    #endregion
}
