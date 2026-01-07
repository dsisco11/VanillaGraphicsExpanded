using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.SSGI;

/// <summary>
/// Captures the lit scene during the Opaque render stage, before first-person models.
/// This provides SSGI with access to the raw lit geometry without SSAO, bloom, or color grading.
/// 
/// Registered at EnumRenderStage.Opaque with RenderOrder 0.75 to capture after world geometry
/// but BEFORE first-person held items/hands are rendered (which typically have higher orders).
/// This prevents hands from contributing to indirect lighting.
/// </summary>
public class SSGISceneCaptureRenderer : IRenderer, IDisposable
{
    #region Constants

    /// <summary>
    /// Render order to capture after world geometry but before first-person models.
    /// VS typically renders: terrain/blocks → entities → particles → held item (higher orders)
    /// </summary>
    private const double RENDER_ORDER = 0.75;
    private const int RENDER_RANGE = 1;

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly SSGIBufferManager bufferManager;

    #endregion

    #region Properties

    /// <summary>
    /// Whether scene capture is enabled. Should match SSGI enabled state.
    /// </summary>
    public bool Enabled { get; set; } = true;

    #endregion

    #region IRenderer Implementation

    public double RenderOrder => RENDER_ORDER;
    public int RenderRange => RENDER_RANGE;

    #endregion

    #region Constructor

    public SSGISceneCaptureRenderer(ICoreClientAPI capi, SSGIBufferManager bufferManager)
    {
        this.capi = capi;
        this.bufferManager = bufferManager;

        // Register at Opaque stage to capture lit geometry before OIT/post-processing
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "ssgi_scene_capture");
    }

    #endregion

    #region Rendering

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!Enabled)
            return;

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        int frameWidth = capi.Render.FrameWidth;
        int frameHeight = capi.Render.FrameHeight;

        // Ensure buffers are allocated (this may be called before SSGI renderer)
        bufferManager.EnsureBuffers(frameWidth, frameHeight);

        // Copy the current scene color to our captured scene texture
        // At this point, primaryFb.ColorTextureIds[0] contains:
        // - Fully lit opaque geometry (direct lighting, shadows applied)
        // - No SSAO (applied later in final.fsh)
        // - No bloom/godrays (applied later)
        // - No color grading (applied later)
        // This is exactly what SSGI needs for sampling indirect radiance
        // Pass FBO ID for blitting (handles format conversion)
        bufferManager.CaptureScene(primaryFb.FboId, primaryFb.DepthTextureId, frameWidth, frameHeight);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        // Nothing to dispose - buffers are managed by SSGIBufferManager
    }

    #endregion
}
