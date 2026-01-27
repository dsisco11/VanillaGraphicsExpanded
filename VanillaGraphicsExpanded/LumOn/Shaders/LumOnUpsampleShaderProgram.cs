using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Upsample pass.
/// Bilateral upsamples half-res indirect diffuse to full resolution.
/// </summary>
public class LumOnUpsampleShaderProgram : GpuProgram
{
    public LumOnUpsampleShaderProgram()
    {
        RegisterUniformBlockBinding("LumOnFrameUBO", LumOnUniformBuffers.FrameBinding, required: true);
    }

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

    // Per-frame state (matrices, sizes, zNear/zFar) is provided via LumOnFrameUBO.

    #region Texture Samplers

    /// <summary>
    /// Half-resolution indirect diffuse texture.
    /// </summary>
    public GpuTexture? IndirectHalf { set => BindTexture2D("indirectHalf", value, 0); }

    /// <summary>
    /// Primary depth texture for edge-aware upsampling.
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 1, GpuSamplers.NearestClamp); }

    /// <summary>
    /// G-buffer normals for edge-aware upsampling.
    /// </summary>
    public int GBufferNormal { set => BindExternalTexture2D("gBufferNormal", value, 2, GpuSamplers.NearestClamp); }

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
