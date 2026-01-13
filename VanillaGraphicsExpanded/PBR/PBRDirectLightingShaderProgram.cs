using System;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Shader program for the PBR direct lighting pass.
/// Renders a fullscreen pass that outputs split radiance buffers:
/// - MRT0: Direct diffuse
/// - MRT1: Direct specular
/// - MRT2: Emissive
/// </summary>
public sealed class PBRDirectLightingShaderProgram : VgeShaderProgram
{
    private int cachedUniformProgramId;
    private int locPointLightsCount = -1;
    private int locPointLights3 = -1;
    private int locPointLightColors3 = -1;

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new PBRDirectLightingShaderProgram
        {
            PassName = "pbr_direct_lighting",
            AssetDomain = "vanillagraphicsexpanded"
        };

        api.Shader.RegisterMemoryShaderProgram("pbr_direct_lighting", instance);
        instance.Initialize(api);
        instance.CompileAndLink();
    }

    #endregion

    protected override void OnAfterCompile()
    {
        CacheUniformLocations();
    }

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

    /// <summary>
    /// Uploads point light arrays and count to the currently-bound program.
    /// Expects GLSL uniforms:
    /// - int pointLightsCount
    /// - vec3 pointLights3[100]
    /// - vec3 pointLightColors3[100]
    /// </summary>
    public void SetPointLights(int count, float[]? pointLights3, float[]? pointLightColors3)
    {
        int clampedCount = Math.Clamp(count, 0, 100);

        EnsureUniformLocations();

        if (locPointLightsCount >= 0)
        {
            GL.Uniform1(locPointLightsCount, clampedCount);
        }

        UploadVec3ArrayUniform(locPointLights3, pointLights3, clampedCount);
        UploadVec3ArrayUniform(locPointLightColors3, pointLightColors3, clampedCount);
    }

    #endregion

    #region Helpers

    private void EnsureUniformLocations()
    {
        // Locations are per-program; refresh if we were recompiled/reloaded.
        if (ProgramId != 0 && cachedUniformProgramId != ProgramId)
        {
            CacheUniformLocations();
        }
    }

    private void CacheUniformLocations()
    {
        cachedUniformProgramId = ProgramId;

        if (cachedUniformProgramId == 0)
        {
            locPointLightsCount = -1;
            locPointLights3 = -1;
            locPointLightColors3 = -1;
            return;
        }

        locPointLightsCount = GL.GetUniformLocation(cachedUniformProgramId, "pointLightsCount");
        locPointLights3 = GL.GetUniformLocation(cachedUniformProgramId, "pointLights3");
        locPointLightColors3 = GL.GetUniformLocation(cachedUniformProgramId, "pointLightColors3");
    }

    private static void UploadVec3ArrayUniform(int location, float[]? data, int vec3Count)
    {
        if (location < 0 || vec3Count <= 0 || data is null || data.Length < 3)
        {
            return;
        }

        int requiredFloats = vec3Count * 3;
        if (data.Length < requiredFloats)
        {
            vec3Count = Math.Min(vec3Count, data.Length / 3);
            if (vec3Count <= 0) return;
        }

        GL.Uniform3(location, vec3Count, data);
    }

    #endregion

    #region Shadows

    public float ShadowRangeNear { set => Uniform("shadowRangeNear", value); }

    public float ShadowRangeFar { set => Uniform("shadowRangeFar", value); }

    public float ShadowZExtendNear { set => Uniform("shadowZExtendNear", value); }

    public float ShadowZExtendFar { set => Uniform("shadowZExtendFar", value); }

    public float DropShadowIntensity { set => Uniform("dropShadowIntensity", value); }

    #endregion
}
