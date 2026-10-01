using System;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Profiling;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Fullscreen direct lighting pass (Opaque stage).
/// Writes split radiance buffers into DirectLightingBufferManager:
/// - DirectDiffuse
/// - DirectSpecular
/// - Emissive
/// </summary>
public sealed class DirectLightingRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = 9.0;
    private const int RenderRangeValue = 1;

    private readonly ICoreClientAPI capi;
    private readonly GBufferManager gBufferManager;
    private readonly DirectLightingBufferManager bufferManager;

    private MeshRef? quadMeshRef;

    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] invModelViewMatrix = new float[16];

    public double RenderOrder => RenderOrderValue;

    public int RenderRange => RenderRangeValue;

    #region Rendering
    /// <summary>Registers direct lighting using the engine view-space light coordinates.</summary>
    public DirectLightingRenderer(
        ICoreClientAPI capi,
        GBufferManager gBufferManager,
        DirectLightingBufferManager bufferManager)
    {
        this.capi = capi;
        this.gBufferManager = gBufferManager;
        this.bufferManager = bufferManager;

        var shader = Rendering.Shaders.GpuShaderPrograms.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        if (shader != null) Rendering.Shaders.GpuShaderPrograms.Preload(capi, [shader]);

        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "pbr_direct_lighting");

        capi.Logger.Notification("[VGE] DirectLightingRenderer registered (Opaque @ 9.0)");
    }

    /// <summary>Reconstructs terrain-relative receivers and renders direct lighting into its split targets.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Opaque || quadMeshRef is null)
        {
            return;
        }

        int screenW = capi.Render.FrameWidth;
        int screenH = capi.Render.FrameHeight;
        if (screenW <= 0 || screenH <= 0)
        {
            return;
        }


        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
        {
            return;
        }

        // Save current FBO + viewport so we can restore engine state.
        // Must happen before EnsureBuffers(), which may recreate/bind/unbind FBOs during resize.
        int prevFbo = GpuFramebuffer.SaveBinding();
        int[] prevViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, prevViewport);

        // Ensure output buffers match current screen size
        if (!bufferManager.EnsureBuffers(screenW, screenH))
        {
            return;
        }

        // Compute inverse matrices
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        MatrixHelper.Invert(capi.Render.CameraMatrixOriginf, invModelViewMatrix);

        // Shader program
        var shader = global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        if (shader is null || !shader.EnsureReady())
        {
            return;
        }

        // Bind output MRT FBO
        bufferManager.BindForRendering();
        GL.Viewport(0, 0, screenW, screenH);

        // Clear outputs (the pass should fully overwrite, but clear is cheap insurance)
        float[] clear = [0f, 0f, 0f, 0f];
        GL.ClearBuffer(ClearBuffer.Color, 0, clear);
        GL.ClearBuffer(ClearBuffer.Color, 1, clear);
        GL.ClearBuffer(ClearBuffer.Color, 2, clear);

        // State for fullscreen pass
        capi.Render.GLDepthMask(false);
        capi.Render.GlToggleBlend(false);



        // Bind input textures
        shader.PrimaryScene = primaryFb.ColorTextureIds[0];
        shader.PrimaryDepth = primaryFb.DepthTextureId;
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

        using var cpuScope = Profiler.BeginScope("PBR.DirectLighting", "Render");
        using (GlGpuProfiler.Instance.Scope("PBR.DirectLighting"))
        {
            shader.Use();
            capi.Render.RenderMesh(quadMeshRef);
        }

        shader.Stop();

        // Restore state
        capi.Render.GLDepthMask(true);

        GpuFramebuffer.RestoreBinding(prevFbo);
        GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);
    }

    #endregion

    #region Lifetime
    /// <summary>Releases the fullscreen mesh and unregisters the lighting callback.</summary>
    public void Dispose()
    {
        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
    #endregion
}
