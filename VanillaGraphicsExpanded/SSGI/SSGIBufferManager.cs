using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

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
    private int ssgiCurrentFboId;
    private int ssgiPreviousFboId;
    private int ssgiCurrentTextureId;
    private int ssgiPreviousTextureId;

    // Blurred SSGI (after spatial blur pass)
    private int ssgiBlurredFboId;
    private int ssgiBlurredTextureId;

    // Depth history buffer for temporal reprojection
    private int depthHistoryTextureId;
    private int depthHistoryFboId; // FBO for blitting depth

    // Captured scene texture (lit geometry before post-processing)
    private int capturedSceneTextureId;
    private int capturedSceneFboId; // FBO for blitting to captured scene

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
    public int CurrentSSGIFboId => ssgiCurrentFboId;

    /// <summary>
    /// OpenGL texture ID for the current frame's SSGI result.
    /// </summary>
    public int CurrentSSGITextureId => ssgiCurrentTextureId;

    /// <summary>
    /// OpenGL texture ID for the previous frame's SSGI result (for temporal filtering).
    /// </summary>
    public int PreviousSSGITextureId => ssgiPreviousTextureId;

    /// <summary>
    /// OpenGL FBO ID for the blurred SSGI buffer.
    /// </summary>
    public int BlurredSSGIFboId => ssgiBlurredFboId;

    /// <summary>
    /// OpenGL texture ID for the blurred SSGI result.
    /// </summary>
    public int BlurredSSGITextureId => ssgiBlurredTextureId;

    /// <summary>
    /// OpenGL texture ID for the previous frame's depth (for temporal reprojection).
    /// </summary>
    public int PreviousDepthTextureId => depthHistoryTextureId;

    /// <summary>
    /// OpenGL texture ID for the captured scene (lit geometry before post-processing).
    /// This is what SSGI samples for indirect radiance.
    /// </summary>
    public int CapturedSceneTextureId => capturedSceneTextureId;

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
        // Swap texture IDs
        (ssgiCurrentTextureId, ssgiPreviousTextureId) = (ssgiPreviousTextureId, ssgiCurrentTextureId);
        
        // Swap FBO IDs
        (ssgiCurrentFboId, ssgiPreviousFboId) = (ssgiPreviousFboId, ssgiCurrentFboId);
    }

    /// <summary>
    /// Copies the current frame's depth buffer to the history texture for next frame's reprojection.
    /// </summary>
    /// <param name="sourceDepthTextureId">The current frame's depth texture ID</param>
    /// <param name="sourceWidth">Width of the source texture</param>
    /// <param name="sourceHeight">Height of the source texture</param>
    public void CopyDepthToHistory(int sourceFboId, int sourceWidth, int sourceHeight)
    {
        if (!isInitialized || depthHistoryTextureId == 0 || depthHistoryFboId == 0)
            return;

        // Only copy if dimensions match to prevent crashes on resize
        if (sourceWidth != lastFullWidth || sourceHeight != lastFullHeight)
            return;

        // Store current framebuffer binding
        int prevFbo = GL.GetInteger(GetPName.FramebufferBinding);

        // Use glBlitFramebuffer to copy depth (handles format conversion)
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sourceFboId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, depthHistoryFboId);
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
        if (!isInitialized || capturedSceneTextureId == 0 || capturedSceneFboId == 0)
            return;

        // Only copy if dimensions match to prevent crashes on resize
        // When window is resized, buffers will be recreated next frame
        if (sourceWidth != lastFullWidth || sourceHeight != lastFullHeight)
            return;

        // Store current framebuffer binding
        int prevFbo = GL.GetInteger(GetPName.FramebufferBinding);

        // Use glBlitFramebuffer to copy scene color (handles format conversion)
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sourceFboId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, capturedSceneFboId);
        GL.BlitFramebuffer(
            0, 0, sourceWidth, sourceHeight,
            0, 0, lastFullWidth, lastFullHeight,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);

        // Restore framebuffer binding
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);

        // On first frame, initialize depth history to prevent sampling uninitialized memory
        if (isFirstFrame && depthHistoryTextureId != 0 && depthHistoryFboId != 0)
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sourceFboId);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, depthHistoryFboId);
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
        ssgiCurrentTextureId = CreateTexture(width, height, PixelInternalFormat.Rgba16f, true);
        ssgiCurrentFboId = CreateFramebuffer(ssgiCurrentTextureId);

        // Create previous SSGI framebuffer and texture (for temporal accumulation)
        ssgiPreviousTextureId = CreateTexture(width, height, PixelInternalFormat.Rgba16f, true);
        ssgiPreviousFboId = CreateFramebuffer(ssgiPreviousTextureId);

        // Create blurred SSGI framebuffer and texture (same resolution as raw SSGI)
        ssgiBlurredTextureId = CreateTexture(width, height, PixelInternalFormat.Rgba16f, true);
        ssgiBlurredFboId = CreateFramebuffer(ssgiBlurredTextureId);

        // Create depth history texture at full resolution (for accurate reprojection)
        // Use Depth24Stencil8 to match VS's primary framebuffer depth format for blitting
        depthHistoryTextureId = CreateTexture(fullWidth, fullHeight, PixelInternalFormat.Depth24Stencil8, false);
        depthHistoryFboId = CreateDepthOnlyFramebuffer(depthHistoryTextureId);

        // Create captured scene texture at full resolution
        // This stores the lit scene before post-processing for SSGI to sample
        capturedSceneTextureId = CreateTexture(fullWidth, fullHeight, PixelInternalFormat.Rgba16f, true);
        capturedSceneFboId = CreateFramebuffer(capturedSceneTextureId);

        isInitialized = true;
        isFirstFrame = true; // Reset first-frame flag for depth history initialization

        capi.Logger.Notification($"[VGE] Created SSGI buffers: {width}x{height} (scale={resolutionScale:F2}), scene capture: {fullWidth}x{fullHeight}");
    }

    private int CreateTexture(int width, int height, PixelInternalFormat internalFormat, bool isColor)
    {
        int textureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        // Determine format and type based on internal format
        PixelFormat format;
        PixelType type;

        if (internalFormat == PixelInternalFormat.Depth24Stencil8)
        {
            format = PixelFormat.DepthStencil;
            type = PixelType.UnsignedInt248;
        }
        else if (!isColor)
        {
            format = PixelFormat.DepthComponent;
            type = PixelType.Float;
        }
        else
        {
            format = PixelFormat.Rgba;
            type = PixelType.Float;
        }

        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            internalFormat,
            width,
            height,
            0,
            format,
            type,
            IntPtr.Zero);

        // Linear filtering for color, nearest for depth
        var filterMode = isColor ? TextureMinFilter.Linear : TextureMinFilter.Nearest;
        var magFilter = isColor ? TextureMagFilter.Linear : TextureMagFilter.Nearest;

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)filterMode);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)magFilter);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindTexture(TextureTarget.Texture2D, 0);
        return textureId;
    }

    private int CreateFramebuffer(int colorTextureId)
    {
        int fboId = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            colorTextureId,
            0);

        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            capi.Logger.Error($"[VGE] SSGI framebuffer incomplete: {status}");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return fboId;
    }

    private int CreateDepthOnlyFramebuffer(int depthTextureId)
    {
        int fboId = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

        // Use DepthStencilAttachment for Depth24Stencil8 format
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthStencilAttachment,
            TextureTarget.Texture2D,
            depthTextureId,
            0);

        // No color buffer for depth-only FBO
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            capi.Logger.Error($"[VGE] SSGI depth history framebuffer incomplete: {status}");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return fboId;
    }

    private void DeleteBuffers()
    {
        if (ssgiCurrentFboId != 0)
        {
            GL.DeleteFramebuffer(ssgiCurrentFboId);
            ssgiCurrentFboId = 0;
        }

        if (ssgiPreviousFboId != 0)
        {
            GL.DeleteFramebuffer(ssgiPreviousFboId);
            ssgiPreviousFboId = 0;
        }

        if (ssgiCurrentTextureId != 0)
        {
            GL.DeleteTexture(ssgiCurrentTextureId);
            ssgiCurrentTextureId = 0;
        }

        if (ssgiPreviousTextureId != 0)
        {
            GL.DeleteTexture(ssgiPreviousTextureId);
            ssgiPreviousTextureId = 0;
        }

        if (ssgiBlurredFboId != 0)
        {
            GL.DeleteFramebuffer(ssgiBlurredFboId);
            ssgiBlurredFboId = 0;
        }

        if (ssgiBlurredTextureId != 0)
        {
            GL.DeleteTexture(ssgiBlurredTextureId);
            ssgiBlurredTextureId = 0;
        }

        if (depthHistoryTextureId != 0)
        {
            GL.DeleteTexture(depthHistoryTextureId);
            depthHistoryTextureId = 0;
        }

        if (depthHistoryFboId != 0)
        {
            GL.DeleteFramebuffer(depthHistoryFboId);
            depthHistoryFboId = 0;
        }

        if (capturedSceneTextureId != 0)
        {
            GL.DeleteTexture(capturedSceneTextureId);
            capturedSceneTextureId = 0;
        }

        if (capturedSceneFboId != 0)
        {
            GL.DeleteFramebuffer(capturedSceneFboId);
            capturedSceneFboId = 0;
        }

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
