using System;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

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
public sealed class PBRDirectLightingShaderProgram : GpuProgram
{
    private PbrDirectLightingParamsUbo? paramsUbo;
    
    // Cached state for compound properties
    private float _zNear, _zFar, _shadowRangeNear, _shadowRangeFar;
    private float _shadowZExtendNear, _shadowZExtendFar, _dropShadowIntensity;

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new PBRDirectLightingShaderProgram
        {
            PassName = "pbr_direct_lighting",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.RegisterUniformBlockBinding(PbrDirectLightingParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object, required: true);
        instance.Initialize(api);
        instance.CompileAndLink();

        api.Shader.RegisterMemoryShaderProgram("pbr_direct_lighting", instance);
    }

    #endregion

    private PbrDirectLightingParamsUbo Params => paramsUbo ??= new PbrDirectLightingParamsUbo();

    private void UploadAndBindParamsUbo()
    {
        Params.BindTo(this, PbrDirectLightingParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
    }

    #region Texture Samplers

    /// <summary>
    /// Primary scene color (baseColor) texture (texture unit 0).
    /// </summary>
    public int PrimaryScene { set => BindExternalTexture2D("primaryScene", value, 0, GpuSamplers.LinearClamp); }

    /// <summary>
    /// Primary depth texture (texture unit 1).
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 1, GpuSamplers.NearestClamp); }

    /// <summary>
    /// G-buffer normal texture (Attachment4) (texture unit 2).
    /// Packed normalWS = n*0.5+0.5
    /// </summary>
    public int GBufferNormal { set => BindExternalTexture2D("gBufferNormal", value, 2, GpuSamplers.NearestClamp); }

    /// <summary>
    /// G-buffer material texture (Attachment5) (texture unit 3).
    /// Contains: Roughness (R), Metallic (G), Emissive (B), Reflectivity (A).
    /// </summary>
    public int GBufferMaterial { set => BindExternalTexture2D("gBufferMaterial", value, 3, GpuSamplers.NearestClamp); }

    /// <summary>
    /// Near shadow map (texture unit 4).
    /// Expected to be the depth texture of EnumFrameBuffer.ShadowmapNear.
    /// </summary>
    public int ShadowMapNear { set => BindExternalTexture2D("shadowMapNear", value, 4, GpuSamplers.ShadowCompareLinearClamp); }

    /// <summary>
    /// Far shadow map (texture unit 5).
    /// Expected to be the depth texture of EnumFrameBuffer.ShadowmapFar.
    /// </summary>
    public int ShadowMapFar { set => BindExternalTexture2D("shadowMapFar", value, 5, GpuSamplers.ShadowCompareLinearClamp); }

    #endregion

    #region Matrices

    public float[] InvProjectionMatrix
    {
        set
        {
            Params.InvProjectionMatrix = value;
            UploadAndBindParamsUbo();
        }
    }

    public float[] InvModelViewMatrix
    {
        set
        {
            Params.InvModelViewMatrix = value;
            UploadAndBindParamsUbo();
        }
    }

    public float[] ToShadowMapSpaceMatrixNear
    {
        set
        {
            Params.ToShadowMapSpaceMatrixNear = value;
            UploadAndBindParamsUbo();
        }
    }

    public float[] ToShadowMapSpaceMatrixFar
    {
        set
        {
            Params.ToShadowMapSpaceMatrixFar = value;
            UploadAndBindParamsUbo();
        }
    }

    #endregion

    #region Z Planes and Shadow Ranges

    /// <summary>
    /// Sets all Z planes and shadow ranges at once (use for batch updates).
    /// </summary>
    public (float zNear, float zFar, float shadowRangeNear, float shadowRangeFar) ZPlanesAndShadowRanges
    {
        set
        {
            _zNear = value.zNear;
            _zFar = value.zFar;
            _shadowRangeNear = value.shadowRangeNear;
            _shadowRangeFar = value.shadowRangeFar;
            Params.ZPlanesAndShadowRanges = value;
            UploadAndBindParamsUbo();
        }
    }

    public float ZNear
    {
        set
        {
            _zNear = value;
            Params.ZPlanesAndShadowRanges = (_zNear, _zFar, _shadowRangeNear, _shadowRangeFar);
            UploadAndBindParamsUbo();
        }
    }

    public float ZFar
    {
        set
        {
            _zFar = value;
            Params.ZPlanesAndShadowRanges = (_zNear, _zFar, _shadowRangeNear, _shadowRangeFar);
            UploadAndBindParamsUbo();
        }
    }

    public float ShadowRangeNear
    {
        set
        {
            _shadowRangeNear = value;
            Params.ZPlanesAndShadowRanges = (_zNear, _zFar, _shadowRangeNear, _shadowRangeFar);
            UploadAndBindParamsUbo();
        }
    }

    public float ShadowRangeFar
    {
        set
        {
            _shadowRangeFar = value;
            Params.ZPlanesAndShadowRanges = (_zNear, _zFar, _shadowRangeNear, _shadowRangeFar);
            UploadAndBindParamsUbo();
        }
    }

    #endregion

    #region Camera

    public Vec3f CameraOriginFloor
    {
        set
        {
            Params.CameraOriginFloor = new System.Numerics.Vector3(value.X, value.Y, value.Z);
            UploadAndBindParamsUbo();
        }
    }

    public Vec3f CameraOriginFrac
    {
        set
        {
            Params.CameraOriginFrac = new System.Numerics.Vector3(value.X, value.Y, value.Z);
            UploadAndBindParamsUbo();
        }
    }

    #endregion

    #region Lighting

    public Vec3f LightDirection
    {
        set
        {
            Params.LightDirection = new System.Numerics.Vector3(value.X, value.Y, value.Z);
            UploadAndBindParamsUbo();
        }
    }

    public Vec3f RgbaAmbientIn
    {
        set
        {
            Params.RgbaAmbientIn = new System.Numerics.Vector3(value.X, value.Y, value.Z);
            UploadAndBindParamsUbo();
        }
    }

    public Vec3f RgbaLightIn
    {
        set
        {
            Params.RgbaLightIn = new System.Numerics.Vector3(value.X, value.Y, value.Z);
            UploadAndBindParamsUbo();
        }
    }

    public int PointLightsCount
    {
        set
        {
            Params.SetPointLights(value, null, null);
            UploadAndBindParamsUbo();
        }
    }

    /// <summary>
    /// Uploads point light arrays and count to the currently-bound program.
    /// Expects GLSL uniforms:
    /// - int pointLightsCount
    /// - vec3 pointLights3[100]
    /// - vec3 pointLightColors3[100]
    /// </summary>
    public void SetPointLights(int count, float[]? pointLights3, float[]? pointLightColors3)
    {
        Params.SetPointLights(count, pointLights3, pointLightColors3);
        UploadAndBindParamsUbo();
    }

    #endregion

    #region Shadows

    public float ShadowZExtendNear
    {
        set
        {
            _shadowZExtendNear = value;
            Params.ShadowExtendAndDrop = (_shadowZExtendNear, _shadowZExtendFar, _dropShadowIntensity);
            UploadAndBindParamsUbo();
        }
    }

    public float ShadowZExtendFar
    {
        set
        {
            _shadowZExtendFar = value;
            Params.ShadowExtendAndDrop = (_shadowZExtendNear, _shadowZExtendFar, _dropShadowIntensity);
            UploadAndBindParamsUbo();
        }
    }

    public float DropShadowIntensity
    {
        set
        {
            _dropShadowIntensity = value;
            Params.ShadowExtendAndDrop = (_shadowZExtendNear, _shadowZExtendFar, _dropShadowIntensity);
            UploadAndBindParamsUbo();
        }
    }

    #endregion
}
