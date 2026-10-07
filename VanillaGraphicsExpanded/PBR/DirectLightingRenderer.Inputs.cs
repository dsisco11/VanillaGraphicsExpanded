using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Assigns current engine view and lighting inputs through the generated shader interface.</summary>
public sealed partial class DirectLightingRenderer
{
    #region Private
    /// <summary>Retains CPU-side inputs for the authoritative draw's existing resource and UBO publication path.</summary>
    private void AssignInputs(PBRDirectLightingShaderProgram shader, FrameBufferRef primaryFb)
    {
        // Bind input textures
        shader.PrimaryScene = primaryFb.ColorTextureIds[0];
        shader.PrimaryDepth = SceneColor.SceneColorParticleCapture.ReceiverDepth(capi, primaryFb.DepthTextureId);
        shader.GBufferNormal = gBufferManager.NormalTextureId;
        shader.GBufferPosition = gBufferManager.PositionTextureId;
        shader.GBufferEnvironment = gBufferManager.EnvironmentTextureId;
        shader.GBufferMaterial = gBufferManager.MaterialTextureId;

        // Shadow maps (depth textures)
        var shadowNearFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.ShadowmapNear];
        var shadowFarFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.ShadowmapFar];
        if (shadowNearFb != null) shader.ShadowMapNear = shadowNearFb.DepthTextureId;
        if (shadowFarFb != null) shader.ShadowMapFar = shadowFarFb.DepthTextureId;

        // Matrices
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.InvModelViewMatrix = invModelViewMatrix;
        shader.ToShadowMapSpaceMatrixNear = capi.Render.ShaderUniforms.ToShadowMapSpaceMatrixNear;
        shader.ToShadowMapSpaceMatrixFar = capi.Render.ShaderUniforms.ToShadowMapSpaceMatrixFar;

        // Z planes
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Lighting
        shader.RgbaAmbientIn = capi.Render.AmbientColor;
        var atmosphere = ModSystems.AtmosphereModSystem.Lighting;
        shader.SetSolarLighting(atmosphere?.Sun ?? System.Numerics.Vector3.UnitY,
            atmosphere?.Solar ?? System.Numerics.Vector3.Zero);

        // Vanilla supplies view-space point lights; the shader compares them with view-space receivers.
        shader.SetPointLights(
            capi.Render.ShaderUniforms.PointLightsCount,
            capi.Render.ShaderUniforms.PointLights3,
            capi.Render.ShaderUniforms.PointLightColors3);

        // Shadow params
        shader.ShadowRangeNear = capi.Render.ShaderUniforms.ShadowRangeNear;
        shader.ShadowRangeFar = capi.Render.ShaderUniforms.ShadowRangeFar;
        shader.ShadowZExtendNear = capi.Render.ShaderUniforms.ShadowZExtendNear;
        shader.ShadowZExtendFar = capi.Render.ShaderUniforms.ShadowZExtendFar;
        shader.DropShadowIntensity = capi.Render.ShaderUniforms.DropShadowIntensity;
    }
    #endregion
}
