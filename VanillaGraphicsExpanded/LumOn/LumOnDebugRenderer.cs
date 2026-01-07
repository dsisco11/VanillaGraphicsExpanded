using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Renders LumOn debug visualizations as a fullscreen overlay at the AfterBlit stage.
/// This ensures debug output is visible on top of all other rendering.
/// 
/// Debug Modes:
/// 0 = Off (no debug rendering)
/// 1 = Probe Grid (shows probe positions with validity coloring)
/// 2 = Probe Depth (shows probe anchor depth as heatmap)
/// 3 = Probe Normals (shows probe anchor normals)
/// 4 = Scene Depth (shows linearized depth buffer)
/// 5 = Scene Normals (shows G-buffer normals)
/// </summary>
public sealed class LumOnDebugRenderer : IRenderer, IDisposable
{
    #region Constants

    private const double DEBUG_RENDER_ORDER = 1.1; // After other debug overlays
    private const int RENDER_RANGE = 1;

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly LumOnConfig config;
    private readonly LumOnBufferManager bufferManager;
    private readonly GBufferManager? gBufferManager;

    private MeshRef? quadMeshRef;

    // Matrix buffer for inverse projection
    private readonly float[] invProjectionMatrix = new float[16];

    #endregion

    #region Properties

    public double RenderOrder => DEBUG_RENDER_ORDER;
    public int RenderRange => RENDER_RANGE;

    #endregion

    #region Constructor

    public LumOnDebugRenderer(
        ICoreClientAPI capi,
        LumOnConfig config,
        LumOnBufferManager bufferManager,
        GBufferManager? gBufferManager)
    {
        this.capi = capi;
        this.config = config;
        this.bufferManager = bufferManager;
        this.gBufferManager = gBufferManager;

        // Create fullscreen quad mesh (-1 to 1 in NDC)
        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        // Register at AfterBlit stage so debug output renders on top
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "lumon_debug");

        capi.Logger.Notification("[LumOn] Debug renderer initialized");
    }

    #endregion

    #region IRenderer

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        // Only render when debug mode is active
        if (config.DebugMode == 0 || !config.Enabled || quadMeshRef is null)
            return;

        // Ensure buffers are initialized
        if (!bufferManager.IsInitialized)
            return;

        var shader = ShaderRegistry.getProgramByName("lumon_debug") as LumOnDebugShaderProgram;
        if (shader is null || shader.LoadError)
            return;

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
            return;

        // Update inverse projection matrix
        ComputeInverseMatrix(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);

        // Disable depth test for fullscreen overlay
        capi.Render.GLDepthMask(false);
        GL.Disable(EnableCap.DepthTest);
        capi.Render.GlToggleBlend(false);

        shader.Use();

        // Bind textures
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager?.NormalTextureId ?? 0;
        shader.ProbeAnchorPosition = bufferManager.ProbeAnchorPositionTextureId;
        shader.ProbeAnchorNormal = bufferManager.ProbeAnchorNormalTextureId;
        shader.RadianceTexture0 = bufferManager.RadianceHistoryTexture0Id;
        shader.IndirectHalf = bufferManager.IndirectHalfTextureId;

        // Pass uniforms
        shader.ScreenSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.ProbeGridSize = new Vec2i(bufferManager.ProbeCountX, bufferManager.ProbeCountY);
        shader.ProbeSpacing = config.ProbeSpacingPx;
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.DebugMode = config.DebugMode;
        shader.InvProjectionMatrix = invProjectionMatrix;

        // Render fullscreen quad
        capi.Render.RenderMesh(quadMeshRef);

        shader.Stop();

        // Restore state
        GL.Enable(EnableCap.DepthTest);
        capi.Render.GLDepthMask(true);
    }

    #endregion

    #region Matrix Utilities

    private static void ComputeInverseMatrix(float[] m, float[] result)
    {
        var matrix = new System.Numerics.Matrix4x4(
            m[0], m[4], m[8], m[12],
            m[1], m[5], m[9], m[13],
            m[2], m[6], m[10], m[14],
            m[3], m[7], m[11], m[15]);

        if (!System.Numerics.Matrix4x4.Invert(matrix, out var inverse))
        {
            for (int i = 0; i < 16; i++)
                result[i] = (i % 5 == 0) ? 1.0f : 0.0f;
            return;
        }

        result[0] = inverse.M11; result[1] = inverse.M21; result[2] = inverse.M31; result[3] = inverse.M41;
        result[4] = inverse.M12; result[5] = inverse.M22; result[6] = inverse.M32; result[7] = inverse.M42;
        result[8] = inverse.M13; result[9] = inverse.M23; result[10] = inverse.M33; result[11] = inverse.M43;
        result[12] = inverse.M14; result[13] = inverse.M24; result[14] = inverse.M34; result[15] = inverse.M44;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
    }

    #endregion
}
