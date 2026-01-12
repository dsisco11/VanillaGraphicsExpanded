using System;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;

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

    // GPU timing
    private int timerQuery;
    private bool timerQueryPending;
    private float lastGpuMs;

    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] invModelViewMatrix = new float[16];

    public double RenderOrder => RenderOrderValue;

    public int RenderRange => RenderRangeValue;

    public DirectLightingRenderer(
        ICoreClientAPI capi,
        GBufferManager gBufferManager,
        DirectLightingBufferManager bufferManager)
    {
        this.capi = capi;
        this.gBufferManager = gBufferManager;
        this.bufferManager = bufferManager;

        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        timerQuery = GL.GenQuery();

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "pbr_direct_lighting");

        capi.Logger.Notification("[VGE] DirectLightingRenderer registered (Opaque @ 9.0)");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Opaque || quadMeshRef is null)
        {
            return;
        }

        CollectTimerQueryResult();

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
        {
            return;
        }

        // Ensure output buffers match current screen size
        if (!bufferManager.EnsureBuffers(capi.Render.FrameWidth, capi.Render.FrameHeight))
        {
            return;
        }

        // Compute inverse matrices
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        MatrixHelper.Invert(capi.Render.CameraMatrixOriginf, invModelViewMatrix);

        // Camera origin split for stable world position reconstruction
        var camPos = capi.World.Player.Entity.CameraPos;
        const double ModuloRange = 4096.0;

        var cameraOriginFloor = new Vec3f(
            (float)(Math.Floor(camPos.X) % ModuloRange),
            (float)(Math.Floor(camPos.Y) % ModuloRange),
            (float)(Math.Floor(camPos.Z) % ModuloRange));

        var cameraOriginFrac = new Vec3f(
            (float)(camPos.X - Math.Floor(camPos.X)),
            (float)(camPos.Y - Math.Floor(camPos.Y)),
            (float)(camPos.Z - Math.Floor(camPos.Z)));

        // Shader program
        var shader = ShaderRegistry.getProgramByName("pbr_direct_lighting") as PBRDirectLightingShaderProgram;
        if (shader is null)
        {
            return;
        }

        // Save current FBO + viewport so we can restore engine state
        int prevFbo = GL.GetInteger(GetPName.FramebufferBinding);
        int[] prevViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, prevViewport);

        // Bind output MRT FBO
        bufferManager.DirectLightingFbo?.Bind();
        GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);

        // Clear outputs (the pass should fully overwrite, but clear is cheap insurance)
        float[] clear = [0f, 0f, 0f, 0f];
        GL.ClearBuffer(ClearBuffer.Color, 0, clear);
        GL.ClearBuffer(ClearBuffer.Color, 1, clear);
        GL.ClearBuffer(ClearBuffer.Color, 2, clear);

        // State for fullscreen pass
        capi.Render.GLDepthMask(false);
        capi.Render.GlToggleBlend(false);

        shader.Use();

        // Bind input textures
        shader.PrimaryScene = primaryFb.ColorTextureIds[0];
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager.NormalTextureId;
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

        // Camera
        shader.CameraOriginFloor = cameraOriginFloor;
        shader.CameraOriginFrac = cameraOriginFrac;

        // Lighting
        shader.LightDirection = capi.Render.ShaderUniforms.SunPosition3D;
        shader.RgbaAmbientIn = capi.Render.AmbientColor;
        shader.RgbaLightIn = ColorUtil.WhiteArgbVec.XYZ;

        // Point lights
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

        BeginTimerQuery();
        capi.Render.RenderMesh(quadMeshRef);
        EndTimerQuery();

        shader.Stop();

        // Restore state
        capi.Render.GLDepthMask(true);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
        GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);
    }

    public void Dispose()
    {
        if (timerQuery != 0)
        {
            GL.DeleteQuery(timerQuery);
            timerQuery = 0;
        }

        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }

    private void BeginTimerQuery()
    {
        if (timerQuery == 0)
        {
            return;
        }

        GL.BeginQuery(QueryTarget.TimeElapsed, timerQuery);
    }

    private void EndTimerQuery()
    {
        if (timerQuery == 0)
        {
            return;
        }

        GL.EndQuery(QueryTarget.TimeElapsed);
        timerQueryPending = true;
    }

    private void CollectTimerQueryResult()
    {
        if (!timerQueryPending || timerQuery == 0)
        {
            return;
        }

        GL.GetQueryObject(timerQuery, GetQueryObjectParam.QueryResultAvailable, out int available);
        if (available == 0)
        {
            return;
        }

        GL.GetQueryObject(timerQuery, GetQueryObjectParam.QueryResult, out long nanoseconds);
        lastGpuMs = nanoseconds / 1_000_000f;
        timerQueryPending = false;
    }
}
