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
    private float resolutionScale = 0.5f;

    // Double-buffered SSGI framebuffers for temporal accumulation
    private int ssgiCurrentFboId;
    private int ssgiPreviousFboId;
    private int ssgiCurrentTextureId;
    private int ssgiPreviousTextureId;

    // Depth history buffer for temporal reprojection
    private int depthHistoryTextureId;

    // Current SSGI buffer dimensions
    private int ssgiWidth;
    private int ssgiHeight;

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
            float clamped = Math.Clamp(value, 0.25f, 1.0f);
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
    /// OpenGL texture ID for the previous frame's depth (for temporal reprojection).
    /// </summary>
    public int PreviousDepthTextureId => depthHistoryTextureId;

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
    public void CopyDepthToHistory(int sourceDepthTextureId)
    {
        if (!isInitialized || depthHistoryTextureId == 0)
            return;

        // Use glCopyImageSubData for efficient GPU-side copy (requires GL 4.3)
        // Fall back to blit if not available
        GL.CopyImageSubData(
            sourceDepthTextureId, ImageTarget.Texture2D, 0, 0, 0, 0,
            depthHistoryTextureId, ImageTarget.Texture2D, 0, 0, 0, 0,
            lastFullWidth, lastFullHeight, 1);
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

        // Create depth history texture at full resolution (for accurate reprojection)
        depthHistoryTextureId = CreateTexture(fullWidth, fullHeight, PixelInternalFormat.DepthComponent32f, false);

        isInitialized = true;

        capi.Logger.Notification($"[VGE] Created SSGI buffers: {width}x{height} (scale={resolutionScale:F2}), depth history: {fullWidth}x{fullHeight}");
    }

    private int CreateTexture(int width, int height, PixelInternalFormat internalFormat, bool isColor)
    {
        int textureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        PixelFormat format = isColor ? PixelFormat.Rgba : PixelFormat.DepthComponent;
        PixelType type = PixelType.Float;

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

        // Linear filtering for SSGI (will be bilaterally upscaled)
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

        if (depthHistoryTextureId != 0)
        {
            GL.DeleteTexture(depthHistoryTextureId);
            depthHistoryTextureId = 0;
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
