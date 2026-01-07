using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.SSGI;

/// <summary>
/// Shader program for SSGI spatial blur pass.
/// Runs at SSGI resolution to spread ray samples before upscaling.
/// Edge-aware blur that respects depth and normal discontinuities.
/// </summary>
public class SSGIBlurShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new SSGIBlurShaderProgram
        {
            PassName = "ssgi_blur",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("ssgi_blur", instance);
        instance.Compile();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Raw SSGI ray march result (texture unit 0).
    /// </summary>
    public int SSGIInput { set => BindTexture2D("ssgiInput", value, 0); }

    /// <summary>
    /// Depth buffer for edge-aware blur (texture unit 1).
    /// </summary>
    public int DepthTexture { set => BindTexture2D("depthTexture", value, 1); }

    /// <summary>
    /// Normal buffer for edge-aware blur (texture unit 2).
    /// </summary>
    public int NormalTexture { set => BindTexture2D("normalTexture", value, 2); }

    #endregion

    #region Uniforms

    /// <summary>
    /// SSGI buffer dimensions.
    /// </summary>
    public Vec2f BufferSize { set => Uniform("bufferSize", value); }

    /// <summary>
    /// Blur radius in pixels (1-4).
    /// </summary>
    public int BlurRadius { set => Uniform("blurRadius", value); }

    /// <summary>
    /// Depth threshold for edge detection (fraction of center depth).
    /// </summary>
    public float DepthThreshold { set => Uniform("depthThreshold", value); }

    /// <summary>
    /// Normal threshold for edge detection.
    /// </summary>
    public float NormalThreshold { set => Uniform("normalThreshold", value); }

    /// <summary>
    /// Z-near plane distance.
    /// </summary>
    public float ZNear { set => Uniform("zNear", value); }

    /// <summary>
    /// Z-far plane distance.
    /// </summary>
    public float ZFar { set => Uniform("zFar", value); }

    #endregion
}
