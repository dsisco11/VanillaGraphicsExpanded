using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.SSGI;

/// <summary>
/// Shader program for compositing SSGI indirect lighting into the final scene.
/// Handles bilateral upscaling when SSGI is rendered at lower resolution.
/// </summary>
public class SSGICompositeShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new SSGICompositeShaderProgram
        {
            PassName = "ssgi_composite",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("ssgi_composite", instance);
        instance.Compile();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// The primary scene color texture to composite SSGI into (texture unit 0).
    /// </summary>
    public int PrimaryScene { set => BindTexture2D("primaryScene", value, 0); }

    /// <summary>
    /// The SSGI indirect lighting texture (texture unit 1).
    /// May be lower resolution than the scene.
    /// </summary>
    public int SSGITexture { set => BindTexture2D("ssgiTexture", value, 1); }

    /// <summary>
    /// The full-resolution depth texture for bilateral upscaling (texture unit 2).
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 2); }

    /// <summary>
    /// The full-resolution normal texture for bilateral upscaling (texture unit 3).
    /// </summary>
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 3); }

    #endregion

    #region Uniforms

    /// <summary>
    /// The full-resolution frame size in pixels.
    /// </summary>
    public Vec2f FrameSize { set => Uniform("frameSize", value); }

    /// <summary>
    /// The SSGI buffer size in pixels (may be lower resolution).
    /// </summary>
    public Vec2f SSGIBufferSize { set => Uniform("ssgiBufferSize", value); }

    /// <summary>
    /// Resolution scale factor (0.25 - 1.0).
    /// </summary>
    public float ResolutionScale { set => Uniform("resolutionScale", value); }

    /// <summary>
    /// Z-near clipping plane for depth linearization.
    /// </summary>
    public float ZNear { set => Uniform("zNear", value); }

    /// <summary>
    /// Z-far clipping plane for depth linearization.
    /// </summary>
    public float ZFar { set => Uniform("zFar", value); }

    /// <summary>
    /// Debug mode for SSGI visualization:
    /// 0 = Composite (normal output)
    /// 1 = SSGI only (indirect lighting)
    /// 2 = Scene only (no SSGI)
    /// </summary>
    public int DebugMode { set => Uniform("debugMode", value); }

    #endregion
}
