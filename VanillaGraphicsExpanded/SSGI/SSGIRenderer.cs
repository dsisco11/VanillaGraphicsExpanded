using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.SSGI;

/// <summary>
/// Renderer for Screen-Space Global Illumination (SSGI).
/// Runs before PBROverlayRenderer to provide indirect lighting that gets composited
/// before PBR direct lighting calculations.
/// 
/// Resolution is configurable via <see cref="ResolutionScale"/> property (0.25 - 1.0).
/// Uses depth-only temporal reprojection for noise reduction.
/// 
/// TODO: Depth-only reprojection will cause ghosting on fast-moving entities.
/// Future work should implement per-object velocity sampling from an entity motion
/// buffer written during the geometry pass. This would require:
/// 1. Adding a velocity G-buffer output (screen-space motion vectors)
/// 2. Modifying entity shaders to output object velocity
/// 3. Blending velocity vectors during temporal reprojection
/// </summary>
public class SSGIRenderer : IRenderer, IDisposable
{
    #region Constants

    /// <summary>
    /// Render order - must be lower than PBROverlayRenderer (1.0) to run first.
    /// </summary>
    private const double RENDER_ORDER = 0.5;
    private const int RENDER_RANGE = 1;

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly SSGIBufferManager bufferManager;
    private MeshRef? quadMeshRef;

    // Matrix buffers
    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] invModelViewMatrix = new float[16];
    private readonly float[] projectionMatrix = new float[16];
    private readonly float[] modelViewMatrix = new float[16];
    private readonly float[] prevViewProjMatrix = new float[16];
    private readonly float[] currentViewProjMatrix = new float[16];

    // Frame counter for temporal jittering
    private int frameIndex;

    // Resolution scale backing field
    private float resolutionScale = 0.25f;

    #endregion

    #region Properties

    /// <summary>
    /// Whether SSGI rendering is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Resolution scale for SSGI buffers (0.05 - 1.0).
    /// Lower values improve performance but reduce quality.
    /// Default: 0.5 (half resolution).
    /// </summary>
    public float ResolutionScale
    {
        get => resolutionScale;
        set
        {
            float clamped = Math.Clamp(value, 0.05f, 1.0f);
            if (Math.Abs(clamped - resolutionScale) > 0.001f)
            {
                resolutionScale = clamped;
                bufferManager.ResolutionScale = clamped;
            }
        }
    }

    /// <summary>
    /// Number of ray march samples per pixel.
    /// Higher values produce better quality but cost more performance.
    /// Default: 6.
    /// </summary>
    public int SampleCount { get; set; } = 12;

    /// <summary>
    /// Maximum ray march distance in blocks.
    /// Default: 16.0.
    /// </summary>
    public float MaxDistance { get; set; } = 20.0f;

    /// <summary>
    /// Ray thickness for intersection testing in blocks.
    /// Larger values are more tolerant but may cause light leaking.
    /// Default: 0.5.
    /// </summary>
    public float RayThickness { get; set; } = 10f;

    /// <summary>
    /// Intensity multiplier for indirect lighting.
    /// Default: 2.0 (boosted for visibility).
    /// </summary>
    public float Intensity { get; set; } = 2.0f;

    /// <summary>
    /// Temporal blend factor for history accumulation.
    /// Higher values (closer to 1.0) accumulate more history for smoother results
    /// but may cause ghosting. Default: 0.95.
    /// </summary>
    public float TemporalBlendFactor { get; set; } = 0.99999f;

    /// <summary>
    /// Whether temporal reprojection is enabled.
    /// </summary>
    public bool TemporalEnabled { get; set; } = true;

    /// <summary>
    /// Number of light bounces (1-3).
    /// Higher values provide more realistic indirect lighting.
    /// Uses temporal accumulation - secondary bounces come from previous frame's SSGI.
    /// Default: 2.
    /// </summary>
    public int BounceCount { get; set; } = 2;

    /// <summary>
    /// Energy loss per bounce (0.3-0.6 typical).
    /// Lower values retain more energy = brighter secondary bounces.
    /// Default: 0.5.
    /// </summary>
    public float BounceAttenuation { get; set; } = 0.2f;

    /// <summary>
    /// Whether spatial blur is enabled.
    /// </summary>
    public bool BlurEnabled { get; set; } = true;

    /// <summary>
    /// Blur radius in pixels (1-4).
    /// Higher values spread light more but cost performance.
    /// Default: 2.
    /// </summary>
    public int BlurRadius { get; set; } = 10;

    /// <summary>
    /// Depth threshold for blur edge detection (fraction of center depth).
    /// Lower values preserve more edges but may leave gaps.
    /// Default: 0.05 (5% of depth).
    /// </summary>
    public float BlurDepthThreshold { get; set; } = 0.05f;

    /// <summary>
    /// Normal threshold for blur edge detection.
    /// Lower values preserve more edges.
    /// Default: 0.5.
    /// </summary>
    public float BlurNormalThreshold { get; set; } = 0.4f;

    /// <summary>
    /// Debug visualization mode:
    /// 0 = Normal composite output
    /// 1 = SSGI indirect lighting only
    /// 2 = Scene without SSGI
    /// </summary>
    public int DebugMode { get; set; } = 0;

    #endregion

    #region IRenderer Implementation

    public double RenderOrder => RENDER_ORDER;
    public int RenderRange => RENDER_RANGE;

    #endregion

    #region Constructor

    public SSGIRenderer(ICoreClientAPI capi, SSGIBufferManager bufferManager)
    {
        this.capi = capi;
        this.bufferManager = bufferManager;

        // Create fullscreen quad mesh
        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        // Register renderer - runs before PBROverlayRenderer
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "ssgi");

        // Initialize previous frame matrix to identity
        for (int i = 0; i < 16; i++)
        {
            prevViewProjMatrix[i] = (i % 5 == 0) ? 1.0f : 0.0f;
        }

        // Register hotkey for SSGI toggle (F8)
        capi.Input.RegisterHotKey(
            "vgessgi",
            "VGE Toggle SSGI",
            GlKeys.F8,
            HotkeyType.DevTool);
        capi.Input.SetHotKeyHandler("vgessgi", OnToggleSSGI);

        // Register hotkey for SSGI debug mode cycling (Shift+F8)
        capi.Input.RegisterHotKey(
            "vgessgidebug",
            "VGE Cycle SSGI Debug Mode",
            GlKeys.F8,
            HotkeyType.DevTool,
            shiftPressed: true);
        capi.Input.SetHotKeyHandler("vgessgidebug", OnCycleDebugMode);
    }

    private bool OnToggleSSGI(KeyCombination keyCombination)
    {
        Enabled = !Enabled;
        string status = Enabled ? "enabled" : "disabled";
        capi?.TriggerIngameError(this, "vgessgi", $"[VGE] SSGI {status}");
        return true;
    }

    private bool OnCycleDebugMode(KeyCombination keyCombination)
    {
        // Cycle through debug modes: 0 (composite) -> 1 (SSGI only) -> 2 (scene only) -> 0
        DebugMode = (DebugMode + 1) % 3;
        string[] modeNames = { "Composite (normal)", "SSGI Only", "Scene Only (no SSGI)" };
        capi?.TriggerIngameError(this, "vgessgidebug", $"[VGE] SSGI Debug: {modeNames[DebugMode]}");
        return true;
    }

    #endregion

    #region Rendering

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (quadMeshRef is null || !Enabled)
            return;

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];

        // Ensure SSGI buffers are allocated
        bufferManager.EnsureBuffers(capi.Render.FrameWidth, capi.Render.FrameHeight);

        // Copy current matrices
        Array.Copy(capi.Render.CurrentProjectionMatrix, projectionMatrix, 16);
        Array.Copy(capi.Render.CameraMatrixOriginf, modelViewMatrix, 16);

        // Compute inverse matrices
        MatrixHelper.Invert(projectionMatrix, invProjectionMatrix);
        MatrixHelper.Invert(modelViewMatrix, invModelViewMatrix);

        // Compute current view-projection matrix for next frame's reprojection
        MatrixHelper.Multiply(projectionMatrix, modelViewMatrix, currentViewProjMatrix);

        // === SSGI Ray March Pass ===
        RenderSSGIPass(primaryFb);

        // === SSGI Blur Pass (optional) ===
        if (BlurEnabled)
        {
            RenderBlurPass(primaryFb);
        }

        // === Debug or Composite Pass ===
        if (DebugMode == 1)
        {
            // Debug mode: render SSGI buffer directly to screen
            RenderDebugPass();
        }
        else
        {
            // Normal mode: composite SSGI into scene
            RenderCompositePass(primaryFb);
        }

        // Store current view-projection matrix for next frame
        Array.Copy(currentViewProjMatrix, prevViewProjMatrix, 16);

        // Swap SSGI buffers for temporal accumulation
        bufferManager.SwapBuffers();

        frameIndex++;
    }

    private void RenderSSGIPass(FrameBufferRef primaryFb)
    {
        var fbo = bufferManager.CurrentSSGIFbo;
        if (fbo is null) return;

        // Bind SSGI framebuffer (current frame)
        fbo.BindWithViewport();
        fbo.Clear();

        var shader = ShaderRegistry.getProgramByName("ssgi") as SSGIShaderProgram;
        if (shader is null)
            return;

        capi.Render.GlToggleBlend(false);
        shader.Use();

        // Bind textures - use captured scene (lit geometry before post-processing)
        shader.CapturedScene = bufferManager.CapturedSceneTex;
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = GBufferManager.Instance?.NormalTextureId ?? 0;
        shader.SSAOTexture = primaryFb.ColorTextureIds[3]; // gPosition.a contains SSAO
        shader.GBufferMaterial = GBufferManager.Instance?.MaterialTextureId ?? 0;
        shader.PreviousSSGI = bufferManager.PreviousSSGITex;
        shader.PreviousDepth = bufferManager.DepthHistoryTex;

        // Pass matrices
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.InvModelViewMatrix = invModelViewMatrix;
        shader.ProjectionMatrix = projectionMatrix;
        shader.ModelViewMatrix = modelViewMatrix;
        shader.PrevViewProjMatrix = prevViewProjMatrix;

        // Pass z-planes
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Pass frame info
        shader.FrameSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.SSGIBufferSize = new Vec2f(bufferManager.SSGIWidth, bufferManager.SSGIHeight);
        shader.ResolutionScale = resolutionScale;
        shader.FrameIndex = frameIndex;

        // SSGI parameters
        shader.SampleCount = SampleCount;
        shader.MaxDistance = MaxDistance;
        shader.RayThickness = RayThickness;
        shader.Intensity = Intensity;

        // Temporal filtering
        shader.TemporalBlendFactor = TemporalBlendFactor;
        shader.TemporalEnabled = TemporalEnabled ? 1 : 0;

        // Multi-bounce
        shader.BounceCount = BounceCount;
        shader.BounceAttenuation = BounceAttenuation;

        // Sky/sun lighting for rays that escape to sky
        var sunPos = capi.World.Calendar.SunPositionNormalized;
        shader.SunPosition = sunPos;
        shader.SunColor = capi.World.Calendar.SunColor;
        shader.AmbientColor = capi.Render.AmbientColor;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();

        // Copy current depth to history buffer for next frame's reprojection
        // Pass FBO ID for blitting (handles format conversion between depth formats)
        bufferManager.CopyDepthToHistory(primaryFb.FboId, capi.Render.FrameWidth, capi.Render.FrameHeight);
    }

    private void RenderBlurPass(FrameBufferRef primaryFb)
    {
        var fbo = bufferManager.BlurredSSGIFbo;
        if (fbo is null) return;

        // Bind blur target framebuffer
        fbo.BindWithViewport();
        fbo.Clear();

        var shader = ShaderRegistry.getProgramByName("ssgi_blur") as SSGIBlurShaderProgram;
        if (shader is null)
            return;

        capi.Render.GlToggleBlend(false);
        shader.Use();

        // Bind textures - use raw SSGI output as input
        shader.SSGIInput = bufferManager.CurrentSSGITex;
        shader.DepthTexture = primaryFb.DepthTextureId;
        shader.NormalTexture = GBufferManager.Instance?.NormalTextureId ?? 0;

        // Pass uniforms
        shader.BufferSize = new Vec2f(bufferManager.SSGIWidth, bufferManager.SSGIHeight);
        shader.BlurRadius = BlurRadius;
        shader.DepthThreshold = BlurDepthThreshold;
        shader.NormalThreshold = BlurNormalThreshold;
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    private void RenderCompositePass(FrameBufferRef primaryFb)
    {
        // Bind primary framebuffer for final composite (VS-managed FBO)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);
        GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);

        var shader = ShaderRegistry.getProgramByName("ssgi_composite") as SSGICompositeShaderProgram;
        if (shader is null)
            return;

        capi.Render.GlToggleBlend(false);
        shader.Use();

        // Bind textures - use blurred SSGI if blur is enabled
        shader.PrimaryScene = primaryFb.ColorTextureIds[0];
        shader.SSGITexture = BlurEnabled ? bufferManager.BlurredSSGITex : bufferManager.CurrentSSGITex;
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = GBufferManager.Instance?.NormalTextureId ?? 0;

        // Pass uniforms
        shader.FrameSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);
        shader.SSGIBufferSize = new Vec2f(bufferManager.SSGIWidth, bufferManager.SSGIHeight);
        shader.ResolutionScale = resolutionScale;
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;
        shader.DebugMode = DebugMode;

        // Render
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();

        // Restore framebuffer state for subsequent passes (HUD, etc.)
        // The game expects the default framebuffer or proper state after AfterBlit
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void RenderDebugPass()
    {
        // Render directly to the default framebuffer (screen)
        // This completely replaces the screen with the SSGI buffer for debugging
        Rendering.GBuffer.Unbind();
        GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        var shader = ShaderRegistry.getProgramByName("ssgi_debug") as SSGIDebugShaderProgram;
        if (shader is null)
            return;

        capi.Render.GlToggleBlend(false);
        shader.Use();

        // Bind the SSGI texture - use blurred if enabled
        shader.SSGITexture = BlurEnabled ? bufferManager.BlurredSSGITex : bufferManager.CurrentSSGITex;
        shader.Boost = 2.0f; // Boost for visibility

        // Render fullscreen quad
        capi.Render.RenderMesh(quadMeshRef);
        shader.Stop();
    }

    #endregion

    #region Matrix Utilities

    // Matrix utilities moved to VanillaGraphicsExpanded.Rendering.MatrixHelper

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }
    }

    #endregion
}
