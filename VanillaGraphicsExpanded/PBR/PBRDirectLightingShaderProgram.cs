using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Shader program for the PBR direct lighting pass.
/// Renders a fullscreen pass that outputs split radiance buffers:
/// - MRT0: Direct diffuse
/// - MRT1: Direct specular
/// - MRT2: Emissive
/// </summary>
public sealed class PBRDirectLightingShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new PBRDirectLightingShaderProgram
        {
            PassName = "pbr_direct_lighting",
            AssetDomain = "vanillagraphicsexpanded"
        };

        api.Shader.RegisterFileShaderProgram("pbr_direct_lighting", instance);
        instance.Compile();
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Primary scene color (baseColor) texture (texture unit 0).
    /// </summary>
    public int PrimaryScene { set => BindTexture2D("primaryScene", value, 0); }

    /// <summary>
    /// Primary depth texture (texture unit 1).
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 1); }

    /// <summary>
    /// G-buffer normal texture (Attachment4) (texture unit 2).
    /// Packed normalWS = n*0.5+0.5
    /// </summary>
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 2); }

    /// <summary>
    /// G-buffer material texture (Attachment5) (texture unit 3).
    /// Contains: Roughness (R), Metallic (G), Emissive (B), Reflectivity (A).
    /// </summary>
    public int GBufferMaterial { set => BindTexture2D("gBufferMaterial", value, 3); }

    /// <summary>
    /// Near shadow map (texture unit 4).
    /// Expected to be the depth texture of EnumFrameBuffer.ShadowmapNear.
    /// </summary>
    public int ShadowMapNear { set => BindTexture2D("shadowMapNear", value, 4); }

    /// <summary>
    /// Far shadow map (texture unit 5).
    /// Expected to be the depth texture of EnumFrameBuffer.ShadowmapFar.
    /// </summary>
    public int ShadowMapFar { set => BindTexture2D("shadowMapFar", value, 5); }

    #endregion

    #region Matrices

    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    public float[] InvModelViewMatrix { set => UniformMatrix("invModelViewMatrix", value); }

    public float[] ToShadowMapSpaceMatrixNear { set => UniformMatrix("toShadowMapSpaceMatrixNear", value); }

    public float[] ToShadowMapSpaceMatrixFar { set => UniformMatrix("toShadowMapSpaceMatrixFar", value); }

    #endregion

    #region Z Planes

    public float ZNear { set => Uniform("zNear", value); }

    public float ZFar { set => Uniform("zFar", value); }

    #endregion

    #region Camera

    public Vec3f CameraOriginFloor { set => Uniform("cameraOriginFloor", value); }

    public Vec3f CameraOriginFrac { set => Uniform("cameraOriginFrac", value); }

    #endregion

    #region Lighting

    public Vec3f LightDirection { set => Uniform("lightDirection", value); }

    public Vec3f RgbaAmbientIn { set => Uniform("rgbaAmbientIn", value); }

    public Vec3f RgbaLightIn { set => Uniform("rgbaLightIn", value); }

    public int PointLightsCount { set => Uniform("pointLightsCount", value); }

    #endregion

    #region Shadows

    public float ShadowRangeNear { set => Uniform("shadowRangeNear", value); }

    public float ShadowRangeFar { set => Uniform("shadowRangeFar", value); }

    public float ShadowZExtendNear { set => Uniform("shadowZExtendNear", value); }

    public float ShadowZExtendFar { set => Uniform("shadowZExtendFar", value); }

    public float DropShadowIntensity { set => Uniform("dropShadowIntensity", value); }

    #endregion
}
