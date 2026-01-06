using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.SSGI;

/// <summary>
/// Shader program for SSGI (Screen-Space Global Illumination) that samples indirect
/// lighting from nearby surfaces using screen-space ray marching.
/// </summary>
public class SSGIShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new SSGIShaderProgram
        {
            PassName = "ssgi",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("ssgi", instance);
        instance.Compile();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// The primary scene color texture (texture unit 0).
    /// Used as the source of indirect radiance (what nearby surfaces are emitting/reflecting).
    /// </summary>
    public int PrimaryScene { set => BindTexture2D("primaryScene", value, 0); }

    /// <summary>
    /// The primary depth texture (texture unit 1).
    /// Used for view-space position reconstruction and ray marching.
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 1); }

    /// <summary>
    /// The G-buffer normal texture (texture unit 2).
    /// World-space normals for hemisphere orientation.
    /// </summary>
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 2); }

    /// <summary>
    /// The vanilla SSAO texture (texture unit 3).
    /// gPosition.a contains the SSAO term, multiplied into indirect lighting.
    /// </summary>
    public int SSAOTexture { set => BindTexture2D("ssaoTexture", value, 3); }

    /// <summary>
    /// Previous frame's SSGI accumulation buffer for temporal reprojection (texture unit 4).
    /// </summary>
    public int PreviousSSGI { set => BindTexture2D("previousSSGI", value, 4); }

    /// <summary>
    /// Previous frame's depth buffer for temporal reprojection (texture unit 5).
    /// </summary>
    public int PreviousDepth { set => BindTexture2D("previousDepth", value, 5); }

    #endregion

    #region Matrix Uniforms

    /// <summary>
    /// The inverse projection matrix for reconstructing view-space positions.
    /// </summary>
    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    /// <summary>
    /// The inverse model-view matrix for reconstructing world-space positions.
    /// </summary>
    public float[] InvModelViewMatrix { set => UniformMatrix("invModelViewMatrix", value); }

    /// <summary>
    /// The projection matrix for screen-space ray marching.
    /// </summary>
    public float[] ProjectionMatrix { set => UniformMatrix("projectionMatrix", value); }

    /// <summary>
    /// The model-view matrix for view-space transformations.
    /// </summary>
    public float[] ModelViewMatrix { set => UniformMatrix("modelViewMatrix", value); }

    /// <summary>
    /// Previous frame's view-projection matrix for temporal reprojection.
    /// </summary>
    public float[] PrevViewProjMatrix { set => UniformMatrix("prevViewProjMatrix", value); }

    #endregion

    #region Z-Plane Uniforms

    /// <summary>
    /// The near clipping plane distance.
    /// </summary>
    public float ZNear { set => Uniform("zNear", value); }

    /// <summary>
    /// The far clipping plane distance.
    /// </summary>
    public float ZFar { set => Uniform("zFar", value); }

    #endregion

    #region Frame Info Uniforms

    /// <summary>
    /// The frame size in pixels (width, height) at full resolution.
    /// </summary>
    public Vec2f FrameSize { set => Uniform("frameSize", value); }

    /// <summary>
    /// The SSGI buffer size in pixels (may be lower resolution than frame).
    /// </summary>
    public Vec2f SSGIBufferSize { set => Uniform("ssgiBufferSize", value); }

    /// <summary>
    /// Resolution scale factor (0.25 - 1.0). Used for bilateral upscaling.
    /// </summary>
    public float ResolutionScale { set => Uniform("resolutionScale", value); }

    /// <summary>
    /// Frame index for temporal jittering (modulo some value for repeating patterns).
    /// </summary>
    public int FrameIndex { set => Uniform("frameIndex", value); }

    #endregion

    #region SSGI Parameters

    /// <summary>
    /// Number of ray march samples per pixel (8-16 recommended).
    /// </summary>
    public int SampleCount { set => Uniform("sampleCount", value); }

    /// <summary>
    /// Maximum ray march distance in view-space units (blocks).
    /// </summary>
    public float MaxDistance { set => Uniform("maxDistance", value); }

    /// <summary>
    /// Ray thickness for intersection testing (blocks).
    /// </summary>
    public float RayThickness { set => Uniform("rayThickness", value); }

    /// <summary>
    /// Intensity multiplier for indirect lighting.
    /// </summary>
    public float Intensity { set => Uniform("intensity", value); }

    #endregion

    #region Temporal Filtering

    /// <summary>
    /// Temporal blend factor (0.0 = current frame only, 1.0 = history only).
    /// Recommended: 0.9-0.95 for good temporal stability.
    /// </summary>
    public float TemporalBlendFactor { set => Uniform("temporalBlendFactor", value); }

    /// <summary>
    /// Whether temporal reprojection is enabled (0 = off, 1 = on).
    /// </summary>
    public int TemporalEnabled { set => Uniform("temporalEnabled", value); }

    #endregion
}
