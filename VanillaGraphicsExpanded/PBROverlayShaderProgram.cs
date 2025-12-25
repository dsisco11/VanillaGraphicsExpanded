using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Shader program for the PBR overlay that exposes uniforms as typed properties.
/// Subclasses ShaderProgram to integrate with the game's shader system.
/// </summary>
public class PBROverlayShaderProgram : ShaderProgram
{
    #region Texture Samplers

    /// <summary>
    /// The primary scene color texture (texture unit 0).
    /// </summary>
    public int PrimaryScene { set => BindTexture2D("primaryScene", value, 0); }

    /// <summary>
    /// The primary depth texture (texture unit 1).
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 1); }

    /// <summary>
    /// The G-buffer normal texture from MRT (texture unit 2).
    /// </summary>
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 2); }

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
    /// The frame size in pixels (width, height).
    /// </summary>
    public Vec2f FrameSize { set => Uniform("frameSize", value); }

    #endregion

    #region Camera and Lighting Uniforms

    /// <summary>
    /// Floor-aligned camera world position (mod 4096) for precision.
    /// </summary>
    public Vec3f CameraOriginFloor { set => Uniform("cameraOriginFloor", value); }

    /// <summary>
    /// Fractional part of camera position (sub-block offset).
    /// </summary>
    public Vec3f CameraOriginFrac { set => Uniform("cameraOriginFrac", value); }

    /// <summary>
    /// Normalized direction vector pointing toward the light.
    /// </summary>
    public Vec3f LightDirection { set => Uniform("lightDirection", value); }
    public Vec3f RgbaLightIn { set => Uniform("rgbaLightIn", value); }
    public Vec3f RgbaAmbientIn { set => Uniform("rgbaAmbientIn", value); }

    #endregion

    #region Debug Mode

    /// <summary>
    /// Debug visualization mode:
    /// 0 = PBR output,
    /// 1 = normals,
    /// 2 = roughness,
    /// 3 = metallic,
    /// 4 = world position,
    /// 5 = depth.
    /// </summary>
    public int DebugMode { set => Uniform("debugMode", value); }

    #endregion

    #region Normal Blur Settings (Teardown-style)

    /// <summary>
    /// Normal blur sample count (Teardown-style golden ratio spiral):
    /// 0 = off, 4, 8, 12, 16 (higher = smoother but slower).
    /// </summary>
    public int NormalQuality { set => Uniform("normalQuality", value); }

    /// <summary>
    /// Normal blur radius in pixels (typically 1.0-3.0).
    /// Larger values create more pronounced beveled edge effect.
    /// </summary>
    public float NormalBlurRadius { set => Uniform("normalBlurRadius", value); }

    #endregion

    #region PBR Distance Falloff Settings

    /// <summary>
    /// Distance (in blocks) where procedural PBR values start to fade out.
    /// </summary>
    public float PbrFalloffStart { set => Uniform("pbrFalloffStart", value); }

    /// <summary>
    /// Distance (in blocks) where procedural PBR values fully fade to defaults.
    /// </summary>
    public float PbrFalloffEnd { set => Uniform("pbrFalloffEnd", value); }

    #endregion
}
