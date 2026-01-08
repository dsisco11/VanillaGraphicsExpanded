using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.SSGI;

/// <summary>
/// Manages framebuffers and textures for SSGI rendering.
/// Handles resolution scaling, double-buffering for temporal accumulation,
/// and depth history for reprojection.
/// </summary>
public sealed class SSGIBufferManager : IDisposable
{
    #region Fields

    private readonly ICoreClientAPI capi;

    // Resolution tracking
    private int lastFullWidth;
    private int lastFullHeight;
    private float resolutionScale = 0.25f;

    // Double-buffered SSGI framebuffers for temporal accumulation
    private DynamicTexture? ssgiCurrentTex;
    private DynamicTexture? ssgiPreviousTex;
    private Rendering.GBuffer? ssgiCurrentFbo;
    private Rendering.GBuffer? ssgiPreviousFbo;

    // Blurred SSGI (after spatial blur pass)
    private DynamicTexture? ssgiBlurredTex;
    private Rendering.GBuffer? ssgiBlurredFbo;

    // Depth history buffer for temporal reprojection
    private DynamicTexture? depthHistoryTex;
    private Rendering.GBuffer? depthHistoryFbo;

    // Captured scene texture (lit geometry before post-processing)
    private DynamicTexture? capturedSceneTex;
    private Rendering.GBuffer? capturedSceneFbo;

    // Current SSGI buffer dimensions
    private int ssgiWidth;
    private int ssgiHeight;

    // Track if this is the first frame (for depth history initialization)
    private bool isFirstFrame = true;

    private bool isInitialized;

    #endregion

    #region Properties

    /// <summary>
    /// Resolution scale factor (0.25 - 1.0).
    /// Setting this property will trigger buffer reallocation on next EnsureBuffers call.
    /// </summary>
    public float ResolutionScale
    {
        get => resolutionScale;
        set
        {
            float clamped = Math.Clamp(value, 0.1f, 1.0f);
            if (Math.Abs(clamped - resolutionScale) > 0.001f)
            {
                resolutionScale = clamped;
                // Force reallocation on next EnsureBuffers
                lastFullWidth = 0;
                lastFullHeight = 0;
            }
        }
    }

    /// <summary>
    /// Current SSGI buffer width in pixels.
    /// </summary>
    public int SSGIWidth => ssgiWidth;

    /// <summary>
    /// Current SSGI buffer height in pixels.
    /// </summary>
    public int SSGIHeight => ssgiHeight;

    /// <summary>
    /// OpenGL FBO ID for the current frame's SSGI buffer.
    /// </summary>
    public int CurrentSSGIFboId => ssgiCurrentFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL texture ID for the current frame's SSGI result.
    /// </summary>
    public int CurrentSSGITextureId => ssgiCurrentTex?.TextureId ?? 0;

    /// <summary>
    /// OpenGL texture ID for the previous frame's SSGI result (for temporal filtering).
    /// </summary>
    public int PreviousSSGITextureId => ssgiPreviousTex?.TextureId ?? 0;

    /// <summary>
    /// OpenGL FBO ID for the blurred SSGI buffer.
    /// </summary>
    public int BlurredSSGIFboId => ssgiBlurredFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL texture ID for the blurred SSGI result.
    /// </summary>
    public int BlurredSSGITextureId => ssgiBlurredTex?.TextureId ?? 0;

    /// <summary>
    /// OpenGL texture ID for the previous frame's depth (for temporal reprojection).
    /// </summary>
    public int PreviousDepthTextureId => depthHistoryTex?.TextureId ?? 0;

    /// <summary>
    /// OpenGL texture ID for the captured scene (lit geometry before post-processing).
    /// This is what SSGI samples for indirect radiance.
    /// </summary>
    public int CapturedSceneTextureId => capturedSceneTex?.TextureId ?? 0;

    /// <summary>
    /// Whether the buffers have been initialized.
    /// </summary>
    public bool IsInitialized => isInitialized;

    #endregion

    #region Constructor

    public SSGIBufferManager(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Ensures SSGI buffers are allocated and sized correctly for the current frame dimensions.
    /// Call this each frame before rendering.
    /// </summary>
    /// <param name="fullWidth">Full-resolution frame width</param>
    /// <param name="fullHeight">Full-resolution frame height</param>
    public void EnsureBuffers(int fullWidth, int fullHeight)
    {
        // Calculate target SSGI buffer size
        int targetWidth = Math.Max(1, (int)(fullWidth * resolutionScale));
        int targetHeight = Math.Max(1, (int)(fullHeight * resolutionScale));

        // Check if we need to (re)allocate
        if (!isInitialized || fullWidth != lastFullWidth || fullHeight != lastFullHeight)
        {
            CreateBuffers(targetWidth, targetHeight, fullWidth, fullHeight);
            lastFullWidth = fullWidth;
            lastFullHeight = fullHeight;
        }
    }

    /// <summary>
    /// Swaps current and previous SSGI buffers for temporal accumulation.
    /// Call this at the end of each frame after rendering.
    /// </summary>
    public void SwapBuffers()
    {
        // Swap textures and FBOs
        (ssgiCurrentTex, ssgiPreviousTex) = (ssgiPreviousTex, ssgiCurrentTex);
        (ssgiCurrentFbo, ssgiPreviousFbo) = (ssgiPreviousFbo, ssgiCurrentFbo);
    }

    /// <summary>
    /// Copies the current frame's depth buffer to the history texture for next frame's reprojection.
    /// </summary>
    /// <param name="sourceFboId">The source framebuffer ID containing depth</param>
    /// <param name="sourceWidth">Width of the source texture</param>
    /// <param name="sourceHeight">Height of the source texture</param>
    public void CopyDepthToHistory(int sourceFboId, int sourceWidth, int sourceHeight)
    {
        if (!isInitialized || depthHistoryFbo == null)
            return;

        // Only copy if dimensions match to prevent crashes on resize
        if (sourceWidth != lastFullWidth || sourceHeight != lastFullHeight)
            return;

        // Store current framebuffer binding
        int prevFbo = GL.GetInteger(GetPName.FramebufferBinding);

        // Use glBlitFramebuffer to copy depth (handles format conversion)
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sourceFboId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, depthHistoryFbo.FboId);
        GL.BlitFramebuffer(
            0, 0, sourceWidth, sourceHeight,
            0, 0, lastFullWidth, lastFullHeight,
            ClearBufferMask.DepthBufferBit,
            BlitFramebufferFilter.Nearest);

        // Restore framebuffer binding
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
    }

    /// <summary>
    /// Captures the current scene color for SSGI to sample.
    /// Called at the end of the Opaque stage before OIT/post-processing.
    /// </summary>
    /// <param name="sourceFboId">The primary framebuffer ID</param>
    /// <param name="sourceDepthTextureId">The primary depth texture ID (for first-frame init)</param>
    /// <param name="sourceWidth">Width of the source textures</param>
    /// <param name="sourceHeight">Height of the source textures</param>
    public void CaptureScene(int sourceFboId, int sourceDepthTextureId, int sourceWidth, int sourceHeight)
    {
        if (!isInitialized || capturedSceneFbo == null)
            return;

        // Only copy if dimensions match to prevent crashes on resize
        if (sourceWidth != lastFullWidth || sourceHeight != lastFullHeight)
            return;

        // Store current framebuffer binding
        int prevFbo = GL.GetInteger(GetPName.FramebufferBinding);

        // Use glBlitFramebuffer to copy scene color (handles format conversion)
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sourceFboId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, capturedSceneFbo.FboId);
        GL.BlitFramebuffer(
            0, 0, sourceWidth, sourceHeight,
            0, 0, lastFullWidth, lastFullHeight,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);

        // Restore framebuffer binding
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);

        // On first frame, initialize depth history to prevent sampling uninitialized memory
        if (isFirstFrame && depthHistoryFbo != null)
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sourceFboId);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, depthHistoryFbo.FboId);
            GL.BlitFramebuffer(
                0, 0, sourceWidth, sourceHeight,
                0, 0, lastFullWidth, lastFullHeight,
                ClearBufferMask.DepthBufferBit,
                BlitFramebufferFilter.Nearest);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
            isFirstFrame = false;
            capi.Logger.Notification("[VGE] SSGI depth history initialized on first frame");
        }
    }

    #endregion

    #region Private Methods

    private void CreateBuffers(int width, int height, int fullWidth, int fullHeight)
    {
        // Delete existing buffers
        DeleteBuffers();

        ssgiWidth = width;
        ssgiHeight = height;

        // Create current SSGI framebuffer and texture
        ssgiCurrentTex = DynamicTexture.Create(width, height, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear);
        ssgiCurrentFbo = Rendering.GBuffer.CreateSingle(ssgiCurrentTex);

        // Create previous SSGI framebuffer and texture (for temporal accumulation)
        ssgiPreviousTex = DynamicTexture.Create(width, height, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear);
        ssgiPreviousFbo = Rendering.GBuffer.CreateSingle(ssgiPreviousTex);

        // Create blurred SSGI framebuffer and texture (same resolution as raw SSGI)
        ssgiBlurredTex = DynamicTexture.Create(width, height, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear);
        ssgiBlurredFbo = Rendering.GBuffer.CreateSingle(ssgiBlurredTex);

        // Create depth history texture at full resolution (for accurate reprojection)
        // Use Depth24Stencil8 to match VS's primary framebuffer depth format for blitting
        depthHistoryTex = DynamicTexture.Create(fullWidth, fullHeight, PixelInternalFormat.Depth24Stencil8);
        depthHistoryFbo = Rendering.GBuffer.CreateDepthOnly(depthHistoryTex);

        // Create captured scene texture at full resolution
        // This stores the lit scene before post-processing for SSGI to sample
        capturedSceneTex = DynamicTexture.Create(fullWidth, fullHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear);
        capturedSceneFbo = Rendering.GBuffer.CreateSingle(capturedSceneTex);

        isInitialized = true;
        isFirstFrame = true; // Reset first-frame flag for depth history initialization

        capi.Logger.Notification($"[VGE] Created SSGI buffers: {width}x{height} (scale={resolutionScale:F2}), scene capture: {fullWidth}x{fullHeight}");
    }

    private void DeleteBuffers()
    {
        // Dispose FBOs first
        ssgiCurrentFbo?.Dispose();
        ssgiPreviousFbo?.Dispose();
        ssgiBlurredFbo?.Dispose();
        depthHistoryFbo?.Dispose();
        capturedSceneFbo?.Dispose();

        ssgiCurrentFbo = null;
        ssgiPreviousFbo = null;
        ssgiBlurredFbo = null;
        depthHistoryFbo = null;
        capturedSceneFbo = null;

        // Dispose textures
        ssgiCurrentTex?.Dispose();
        ssgiPreviousTex?.Dispose();
        ssgiBlurredTex?.Dispose();
        depthHistoryTex?.Dispose();
        capturedSceneTex?.Dispose();

        ssgiCurrentTex = null;
        ssgiPreviousTex = null;
        ssgiBlurredTex = null;
        depthHistoryTex = null;
        capturedSceneTex = null;

        isInitialized = false;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        DeleteBuffers();
    }

    #endregion
}
