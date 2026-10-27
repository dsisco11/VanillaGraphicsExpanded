using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Assigns current engine view and lighting inputs through the generated shader interface.</summary>
public sealed partial class DirectLightingRenderer
{
    private BorrowedTexture? shadowNearInput, shadowFarInput;
    #region Private
    /// <summary>Retains CPU-side inputs for the authoritative draw's existing resource and UBO publication path.</summary>
    private void AssignInputs(PBRDirectLightingShaderProgram shader, FrameBufferRef primaryFb)
    {
        // Bind input textures
        shader.PrimaryScene = gBufferManager.PrimaryColorTexture;
        shader.PrimaryDepth = SceneColor.SceneColorParticleCapture.ReceiverDepth(capi, gBufferManager!.PrimaryDepthTexture);
        shader.GBufferSurface = gBufferManager.SurfaceTexture;
        shader.GBufferPosition = gBufferManager.PositionTexture;

        // Shadow maps (depth textures)
        var shadowNearFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.ShadowmapNear];
        var shadowFarFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.ShadowmapFar];
        shader.ShadowMapNear = shadowNearFb is { DepthTextureId: > 0 }
            ? BorrowedTexture.GetOrReplace(ref shadowNearInput, shadowNearFb.DepthTextureId) : default(GpuTextureRef);
        shader.ShadowMapFar = shadowFarFb is { DepthTextureId: > 0 }
            ? BorrowedTexture.GetOrReplace(ref shadowFarInput, shadowFarFb.DepthTextureId) : default(GpuTextureRef);

        // Matrices
        shader.ToShadowMapSpaceMatrixNear = capi.Render.ShaderUniforms.ToShadowMapSpaceMatrixNear;
        shader.ToShadowMapSpaceMatrixFar = capi.Render.ShaderUniforms.ToShadowMapSpaceMatrixFar;

        // Z planes

        // Lighting
        shader.RgbaAmbientIn = capi.Render.AmbientColor;
        var atmosphere = ModSystems.AtmosphereModSystem.Lighting;
        shader.SetSolarLighting(atmosphere?.DirectDirection ?? System.Numerics.Vector3.UnitY,
            atmosphere?.DirectIrradiance ?? System.Numerics.Vector3.Zero,
            atmosphere?.DirectAngularRadius ?? Atmosphere.AtmosphereSolarDisk.AngularRadius);

        // Shadow params
        shader.ShadowRangeNear = capi.Render.ShaderUniforms.ShadowRangeNear;
        shader.ShadowRangeFar = capi.Render.ShaderUniforms.ShadowRangeFar;
        shader.ShadowZExtendNear = capi.Render.ShaderUniforms.ShadowZExtendNear;
        shader.ShadowZExtendFar = capi.Render.ShaderUniforms.ShadowZExtendFar;
        shader.DropShadowIntensity = capi.Render.ShaderUniforms.DropShadowIntensity;
    }
    #endregion
}
