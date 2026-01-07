using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Manages GPU textures for LumOn probe grid and radiance cache.
/// Creates and maintains framebuffers for:
/// - Probe anchor positions and normals
/// - Radiance cache (SH coefficients) with double-buffering for temporal
/// - Indirect diffuse output at half and full resolution
/// </summary>
public sealed class LumOnBufferManager : IDisposable
{
    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly LumOnConfig config;

    // Screen dimensions tracking
    private int lastScreenWidth;
    private int lastScreenHeight;

    // Probe grid dimensions (computed from screen size and spacing)
    private int probeCountX;
    private int probeCountY;

    // ═══════════════════════════════════════════════════════════════
    // Probe Anchor Buffers
    // ═══════════════════════════════════════════════════════════════

    // ProbeAnchor[0]: posVS.xyz, valid (0/1)
    // ProbeAnchor[1]: normalVS.xyz, reserved
    private int probeAnchorFboId;
    private int probeAnchorPositionTextureId;
    private int probeAnchorNormalTextureId;

    // ═══════════════════════════════════════════════════════════════
    // Radiance Cache Buffers (Double-buffered for temporal)
    // ═══════════════════════════════════════════════════════════════

    // Current frame radiance (write target)
    // [0]: SH coeff 0 (R,G,B), coeff 1.R
    // [1]: SH coeff 1 (G,B), coeff 2 (R,G), coeff 2.B, coeff 3 (R,G,B)
    private int radianceCurrentFboId;
    private int radianceCurrentTexture0Id;
    private int radianceCurrentTexture1Id;

    // History radiance (read source, then swap)
    private int radianceHistoryFboId;
    private int radianceHistoryTexture0Id;
    private int radianceHistoryTexture1Id;

    // ═══════════════════════════════════════════════════════════════
    // Indirect Diffuse Output Buffers
    // ═══════════════════════════════════════════════════════════════

    // Half-resolution gather output
    private int indirectHalfFboId;
    private int indirectHalfTextureId;
    private int halfResWidth;
    private int halfResHeight;

    // Full-resolution upsampled output
    private int indirectFullFboId;
    private int indirectFullTextureId;

    // Double-buffer swap index (0 or 1)
    private int currentBufferIndex;

    private bool isInitialized;

    #endregion

    #region Properties

    /// <summary>
    /// Number of probes horizontally in the grid.
    /// </summary>
    public int ProbeCountX => probeCountX;

    /// <summary>
    /// Number of probes vertically in the grid.
    /// </summary>
    public int ProbeCountY => probeCountY;

    /// <summary>
    /// OpenGL FBO ID for probe anchor pass output.
    /// </summary>
    public int ProbeAnchorFboId => probeAnchorFboId;

    /// <summary>
    /// OpenGL texture ID for probe anchor positions (posVS.xyz, valid).
    /// </summary>
    public int ProbeAnchorPositionTextureId => probeAnchorPositionTextureId;

    /// <summary>
    /// OpenGL texture ID for probe anchor normals (normalVS.xyz, reserved).
    /// </summary>
    public int ProbeAnchorNormalTextureId => probeAnchorNormalTextureId;

    /// <summary>
    /// OpenGL FBO ID for current frame radiance output.
    /// </summary>
    public int RadianceCurrentFboId => radianceCurrentFboId;

    /// <summary>
    /// OpenGL texture ID for current radiance SH coefficients (set 0).
    /// </summary>
    public int RadianceCurrentTexture0Id => radianceCurrentTexture0Id;

    /// <summary>
    /// OpenGL texture ID for current radiance SH coefficients (set 1).
    /// </summary>
    public int RadianceCurrentTexture1Id => radianceCurrentTexture1Id;

    /// <summary>
    /// OpenGL FBO ID for history radiance (read during temporal blend).
    /// </summary>
    public int RadianceHistoryFboId => radianceHistoryFboId;

    /// <summary>
    /// OpenGL texture ID for history radiance SH coefficients (set 0).
    /// </summary>
    public int RadianceHistoryTexture0Id => radianceHistoryTexture0Id;

    /// <summary>
    /// OpenGL texture ID for history radiance SH coefficients (set 1).
    /// </summary>
    public int RadianceHistoryTexture1Id => radianceHistoryTexture1Id;

    /// <summary>
    /// OpenGL FBO ID for half-resolution indirect diffuse output.
    /// </summary>
    public int IndirectHalfFboId => indirectHalfFboId;

    /// <summary>
    /// OpenGL texture ID for half-resolution indirect diffuse.
    /// </summary>
    public int IndirectHalfTextureId => indirectHalfTextureId;

    /// <summary>
    /// OpenGL FBO ID for full-resolution indirect diffuse output.
    /// </summary>
    public int IndirectFullFboId => indirectFullFboId;

    /// <summary>
    /// OpenGL texture ID for full-resolution indirect diffuse (final output).
    /// </summary>
    public int IndirectFullTextureId => indirectFullTextureId;

    /// <summary>
    /// Half-resolution buffer width.
    /// </summary>
    public int HalfResWidth => halfResWidth;

    /// <summary>
    /// Half-resolution buffer height.
    /// </summary>
    public int HalfResHeight => halfResHeight;

    /// <summary>
    /// Whether buffers have been initialized.
    /// </summary>
    public bool IsInitialized => isInitialized;

    #endregion

    #region Constructor

    public LumOnBufferManager(ICoreClientAPI capi, LumOnConfig config)
    {
        this.capi = capi;
        this.config = config;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Ensures LumOn buffers are allocated and sized correctly for the current screen dimensions.
    /// Call this each frame before rendering.
    /// </summary>
    /// <param name="screenWidth">Full-resolution screen width</param>
    /// <param name="screenHeight">Full-resolution screen height</param>
    public void EnsureBuffers(int screenWidth, int screenHeight)
    {
        if (!isInitialized || screenWidth != lastScreenWidth || screenHeight != lastScreenHeight)
        {
            CreateBuffers(screenWidth, screenHeight);
            lastScreenWidth = screenWidth;
            lastScreenHeight = screenHeight;
        }
    }

    /// <summary>
    /// Swap current/history radiance buffers for temporal accumulation.
    /// Called after temporal pass completes.
    /// </summary>
    public void SwapRadianceBuffers()
    {
        // Swap FBO IDs
        (radianceCurrentFboId, radianceHistoryFboId) = (radianceHistoryFboId, radianceCurrentFboId);

        // Swap texture IDs (set 0)
        (radianceCurrentTexture0Id, radianceHistoryTexture0Id) = (radianceHistoryTexture0Id, radianceCurrentTexture0Id);

        // Swap texture IDs (set 1)
        (radianceCurrentTexture1Id, radianceHistoryTexture1Id) = (radianceHistoryTexture1Id, radianceCurrentTexture1Id);

        currentBufferIndex = 1 - currentBufferIndex;
    }

    #endregion

    #region Private Methods

    private void CreateBuffers(int screenWidth, int screenHeight)
    {
        // Delete existing buffers
        DeleteBuffers();

        // Calculate probe grid dimensions
        probeCountX = (int)Math.Ceiling((float)screenWidth / config.ProbeSpacingPx);
        probeCountY = (int)Math.Ceiling((float)screenHeight / config.ProbeSpacingPx);

        // Calculate half-res dimensions
        halfResWidth = config.HalfResolution ? screenWidth / 2 : screenWidth;
        halfResHeight = config.HalfResolution ? screenHeight / 2 : screenHeight;

        // ═══════════════════════════════════════════════════════════════
        // Create Probe Anchor Buffers
        // ═══════════════════════════════════════════════════════════════

        probeAnchorPositionTextureId = CreateTexture(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        probeAnchorNormalTextureId = CreateTexture(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        probeAnchorFboId = CreateFramebufferMRT(
            [probeAnchorPositionTextureId, probeAnchorNormalTextureId]);

        // ═══════════════════════════════════════════════════════════════
        // Create Radiance Cache Buffers (Current)
        // ═══════════════════════════════════════════════════════════════

        radianceCurrentTexture0Id = CreateTexture(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        radianceCurrentTexture1Id = CreateTexture(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        radianceCurrentFboId = CreateFramebufferMRT(
            [radianceCurrentTexture0Id, radianceCurrentTexture1Id]);

        // ═══════════════════════════════════════════════════════════════
        // Create Radiance Cache Buffers (History)
        // ═══════════════════════════════════════════════════════════════

        radianceHistoryTexture0Id = CreateTexture(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        radianceHistoryTexture1Id = CreateTexture(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        radianceHistoryFboId = CreateFramebufferMRT(
            [radianceHistoryTexture0Id, radianceHistoryTexture1Id]);

        // ═══════════════════════════════════════════════════════════════
        // Create Indirect Diffuse Output Buffers
        // ═══════════════════════════════════════════════════════════════

        indirectHalfTextureId = CreateTexture(halfResWidth, halfResHeight, PixelInternalFormat.Rgba16f);
        indirectHalfFboId = CreateFramebuffer(indirectHalfTextureId);

        indirectFullTextureId = CreateTexture(screenWidth, screenHeight, PixelInternalFormat.Rgba16f);
        indirectFullFboId = CreateFramebuffer(indirectFullTextureId);

        isInitialized = true;
        currentBufferIndex = 0;

        capi.Logger.Notification(
            $"[LumOn] Created buffers: {probeCountX}x{probeCountY} probes, " +
            $"spacing={config.ProbeSpacingPx}px, halfRes={halfResWidth}x{halfResHeight}");
    }

    private int CreateTexture(int width, int height, PixelInternalFormat internalFormat)
    {
        int textureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            internalFormat,
            width,
            height,
            0,
            PixelFormat.Rgba,
            PixelType.Float,
            IntPtr.Zero);

        // Use nearest filtering for probe textures (no interpolation between probes)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
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
            capi.Logger.Error($"[LumOn] Framebuffer incomplete: {status}");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return fboId;
    }

    private int CreateFramebufferMRT(int[] colorTextureIds)
    {
        int fboId = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

        var drawBuffers = new DrawBuffersEnum[colorTextureIds.Length];

        for (int i = 0; i < colorTextureIds.Length; i++)
        {
            var attachment = FramebufferAttachment.ColorAttachment0 + i;
            GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                attachment,
                TextureTarget.Texture2D,
                colorTextureIds[i],
                0);
            drawBuffers[i] = DrawBuffersEnum.ColorAttachment0 + i;
        }

        GL.DrawBuffers(colorTextureIds.Length, drawBuffers);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            capi.Logger.Error($"[LumOn] MRT Framebuffer incomplete: {status}");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return fboId;
    }

    private void DeleteBuffers()
    {
        // Delete Probe Anchor buffers
        DeleteFramebufferAndTextures(ref probeAnchorFboId,
            ref probeAnchorPositionTextureId, ref probeAnchorNormalTextureId);

        // Delete Radiance Current buffers
        DeleteFramebufferAndTextures(ref radianceCurrentFboId,
            ref radianceCurrentTexture0Id, ref radianceCurrentTexture1Id);

        // Delete Radiance History buffers
        DeleteFramebufferAndTextures(ref radianceHistoryFboId,
            ref radianceHistoryTexture0Id, ref radianceHistoryTexture1Id);

        // Delete Indirect Half buffer
        DeleteFramebufferAndTexture(ref indirectHalfFboId, ref indirectHalfTextureId);

        // Delete Indirect Full buffer
        DeleteFramebufferAndTexture(ref indirectFullFboId, ref indirectFullTextureId);

        isInitialized = false;
    }

    private void DeleteFramebufferAndTexture(ref int fboId, ref int textureId)
    {
        if (fboId != 0)
        {
            GL.DeleteFramebuffer(fboId);
            fboId = 0;
        }

        if (textureId != 0)
        {
            GL.DeleteTexture(textureId);
            textureId = 0;
        }
    }

    private void DeleteFramebufferAndTextures(ref int fboId, ref int textureId0, ref int textureId1)
    {
        if (fboId != 0)
        {
            GL.DeleteFramebuffer(fboId);
            fboId = 0;
        }

        if (textureId0 != 0)
        {
            GL.DeleteTexture(textureId0);
            textureId0 = 0;
        }

        if (textureId1 != 0)
        {
            GL.DeleteTexture(textureId1);
            textureId1 = 0;
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        DeleteBuffers();
    }

    #endregion
}
