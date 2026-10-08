using VanillaGraphicsExpanded.Rendering.Contracts;
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
[ShaderProgram("Contract", "pbr_direct_lighting", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_direct_lighting.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_direct_lighting.fsh")]
public sealed partial class PBRDirectLightingShaderProgram : GpuProgram, IPBRDirectLightingShaderProgramBindings
{


    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    // Cached state for compound properties
    private float _zNear, _zFar, _shadowRangeNear, _shadowRangeFar;
    private float _shadowZExtendNear, _shadowZExtendFar, _dropShadowIntensity;

    protected override GpuProgramLayout CreateLayout() => new PbrDirectLightingProgramLayout();

    private PbrDirectLightingProgramLayout Layout => (PbrDirectLightingProgramLayout)ProgramLayout;

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new PBRDirectLightingShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    /// <summary>Exposes retained parameters with an owner mutation guard.</summary>
    private PbrDirectLightingParamsUbo Params
    {
        get
        {
            var parameters = Layout.Params;
            parameters.SetWriteGuard(RequireInputMutation);
            return parameters;
        }
    }

#region Texture Samplers

    /// <summary>
    /// Primary scene color (baseColor) texture (texture unit 0).
    /// </summary>
    public partial int PrimaryScene { set; }

    /// <summary>
    /// Primary depth texture (texture unit 1).
    /// </summary>
    public partial int PrimaryDepth { set; }

    /// <summary>Unbiased first-person view-space positions, independent of visibility depth.</summary>
    public partial int GBufferPosition { set; }

    /// <summary>Normal, material and environmental-irradiance layers for deferred lighting.</summary>
    public partial GpuTexture? GBufferSurface { set; }

    /// <summary>
    /// Near shadow map (texture unit 4).
    /// Expected to be the depth texture of EnumFrameBuffer.ShadowmapNear.
    /// </summary>
    public partial int ShadowMapNear { set; }

    /// <summary>
    /// Far shadow map (texture unit 5).
    /// Expected to be the depth texture of EnumFrameBuffer.ShadowmapFar.
    /// </summary>
    public partial int ShadowMapFar { set; }

    #endregion

    #region Matrices

    public float[] InvProjectionMatrix
    {
        set
        {
            RequireInputMutation();
            Params.InvProjectionMatrix = value;
        }
    }

    public float[] InvModelViewMatrix
    {
        set
        {
            RequireInputMutation();
            Params.InvModelViewMatrix = value;
        }
    }

    public float[] ToShadowMapSpaceMatrixNear
    {
        set
        {
            RequireInputMutation();
            Params.ToShadowMapSpaceMatrixNear = value;
        }
    }

    public float[] ToShadowMapSpaceMatrixFar
    {
        set
        {
            RequireInputMutation();
            Params.ToShadowMapSpaceMatrixFar = value;
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
            RequireInputMutation();
            _zNear = value.zNear;
            _zFar = value.zFar;
            _shadowRangeNear = value.shadowRangeNear;
            _shadowRangeFar = value.shadowRangeFar;
            Params.ZPlanesAndShadowRanges = value;
        }
    }

    public float ZNear
    {
        set
        {
            RequireInputMutation();
            _zNear = value;
            Params.ZPlanesAndShadowRanges = (_zNear, _zFar, _shadowRangeNear, _shadowRangeFar);
        }
    }

    public float ZFar
    {
        set
        {
            RequireInputMutation();
            _zFar = value;
            Params.ZPlanesAndShadowRanges = (_zNear, _zFar, _shadowRangeNear, _shadowRangeFar);
        }
    }

    public float ShadowRangeNear
    {
        set
        {
            RequireInputMutation();
            _shadowRangeNear = value;
            Params.ZPlanesAndShadowRanges = (_zNear, _zFar, _shadowRangeNear, _shadowRangeFar);
        }
    }

    public float ShadowRangeFar
    {
        set
        {
            RequireInputMutation();
            _shadowRangeFar = value;
            Params.ZPlanesAndShadowRanges = (_zNear, _zFar, _shadowRangeNear, _shadowRangeFar);
        }
    }

    #endregion

    #region Lighting

    public Vec3f LightDirection
    {
        set
        {
            RequireInputMutation();
            Params.LightDirection = new System.Numerics.Vector3(value.X, value.Y, value.Z);
        }
    }

    public Vec3f RgbaAmbientIn
    {
        set
        {
            RequireInputMutation();
            Params.RgbaAmbientIn = new System.Numerics.Vector3(value.X, value.Y, value.Z);
        }
    }

    /// <summary>Supplies physical solar irradiance without changing the engine's point-light calibration.</summary>
    internal void SetSolarIrradiance(System.Numerics.Vector3 value)
    {
        Params.RgbaLightIn = value;
    }

    /// <summary>Publishes direction and irradiance together from the same atmospheric generation.</summary>
    internal void SetSolarLighting(System.Numerics.Vector3 direction, System.Numerics.Vector3 irradiance)
    {
        Params.LightDirection = direction;
        Params.RgbaLightIn = irradiance;
    }

    /// <summary>Sets physical solar irradiance using the engine vector API.</summary>
    public Vec3f RgbaLightIn
    {
        set
        {
            RequireInputMutation();
            Params.RgbaLightIn = new System.Numerics.Vector3(value.X, value.Y, value.Z);
        }
    }

    public int PointLightsCount
    {
        set
        {
            RequireInputMutation();
            Params.SetPointLights(value, null, null);
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
    }

    #endregion

    #region Shadows

    public float ShadowZExtendNear
    {
        set
        {
            RequireInputMutation();
            _shadowZExtendNear = value;
            Params.ShadowExtendAndDrop = (_shadowZExtendNear, _shadowZExtendFar, _dropShadowIntensity);
        }
    }

    public float ShadowZExtendFar
    {
        set
        {
            RequireInputMutation();
            _shadowZExtendFar = value;
            Params.ShadowExtendAndDrop = (_shadowZExtendNear, _shadowZExtendFar, _dropShadowIntensity);
        }
    }

    public float DropShadowIntensity
    {
        set
        {
            RequireInputMutation();
            _dropShadowIntensity = value;
            Params.ShadowExtendAndDrop = (_shadowZExtendNear, _shadowZExtendFar, _dropShadowIntensity);
        }
    }

    #endregion
    #region Binding sources
    /// <summary>Supplies packed parameters for one publication per use.</summary>
    CpuUniformBuffer IPBRDirectLightingShaderProgramBindings.Parameters => Params;
    #endregion
}
