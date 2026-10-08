using System;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Manages GPU textures for the PBR direct lighting pass.
/// Owns one RGBA16F array containing diffuse, specular and emissive radiance,
/// with a separate framebuffer output attached to each layer.
///
/// All outputs are linear, pre-tonemap HDR.
/// </summary>
public sealed class DirectLightingBufferManager : IDisposable
{

    #region Static Instance

    /// <summary>
    /// Singleton instance accessible from renderers.
    /// </summary>
    public static DirectLightingBufferManager? Instance { get; private set; }

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly Action unregisterResize;

    // Screen dimensions tracking
    private int lastScreenWidth;
    private int lastScreenHeight;

    private DirectLightingTargets? targets;

    private bool isInitialized;

    #endregion

    #region Properties

    /// <summary>
    /// Whether buffers have been created and are ready for use.
    /// </summary>
    public bool IsInitialized => isInitialized;

    /// <summary>Supplies diffuse, specular and emissive layers in one owned radiance array.</summary>
    public Texture3D? Radiance => targets?.Radiance;

    /// <summary>
    /// Framebuffer for direct lighting MRT output.
    /// Attachment0 = DirectDiffuse, Attachment1 = DirectSpecular, Attachment2 = Emissive.
    /// </summary>
    public GpuFramebuffer? DirectLightingFbo => targets?.Framebuffer;

    #endregion

    #region Constructor / Destructor

    /// <summary>Registers owned lighting targets after primary attachment setup.</summary>
    public DirectLightingBufferManager(ICoreClientAPI capi)
    {
        this.capi = capi;
        Instance = this;
        unregisterResize = ScreenResourceManager.Register(
            ScreenResourceManager.DirectLightingOrder,
            OnScreenResized);
    }

    /// <summary>Resizes owned targets using the newly published primary framebuffer dimensions.</summary>
    private void OnScreenResized()
    {
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is not null)
        {
            EnsureBuffers(primaryFb.Width, primaryFb.Height);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Ensures direct lighting buffers exist and match the current screen size.
    /// Call this before rendering to handle resize events.
    /// </summary>
    /// <param name="screenWidth">Current screen width</param>
    /// <param name="screenHeight">Current screen height</param>
    /// <returns>True if buffers are valid and ready to use</returns>
    public bool EnsureBuffers(int screenWidth, int screenHeight)
    {
        // These attachments remain exclusively VGE-owned across engine rebuilds; their owner
        // tracks lifetime without querying the driver for every texture on every frame.
        bool resourcesValid = isInitialized
            && targets?.Framebuffer is { IsValid: true }
            && targets?.Radiance is { IsValid: true };

        if (!resourcesValid)
        {
            CreateBuffers(screenWidth, screenHeight);
            lastScreenWidth = screenWidth;
            lastScreenHeight = screenHeight;
        }
        else if (screenWidth != lastScreenWidth || screenHeight != lastScreenHeight)
        {
            CreateBuffers(screenWidth, screenHeight);
            lastScreenWidth = screenWidth;
            lastScreenHeight = screenHeight;
        }

        return isInitialized && targets?.Framebuffer is { IsValid: true };
    }

    /// <summary>
    /// Unbinds the direct lighting FBO.
    /// </summary>
    public void Unbind()
    {
        GpuFramebuffer.Unbind();
    }

    /// <summary>
    /// Clears all direct lighting buffers to black.
    /// </summary>
    public void ClearBuffers()
    {
        if (targets?.Framebuffer is not { IsValid: true } framebuffer)
            return;

        using var bindings = StateCache.Current.BindFramebufferScope();
        framebuffer.Bind();
        framebuffer.Clear(0, 0, 0, 0);
    }

    #endregion

    #region Private Methods

    /// <summary>Replaces the owned allocation while preserving engine framebuffer state.</summary>
    private void CreateBuffers(int width, int height)
    {
        DeleteBuffers();
        capi.Logger.Notification($"[VGE] Creating direct lighting buffers: {width}x{height}");
        targets = new DirectLightingTargets(width, height);
        isInitialized = targets.IsValid;
        if (!isInitialized)
        {
            capi.Logger.Error("[VGE] Failed to create complete direct lighting targets");
            DeleteBuffers();
        }
    }

    /// <summary>Retires owned targets and withdraws readiness.</summary>
    private void DeleteBuffers()
    {
        targets?.Dispose();
        targets = null;
        isInitialized = false;
    }

    #endregion

    #region IDisposable

    /// <summary>Unregisters screen notifications and releases the owned allocation.</summary>
    public void Dispose()
    {
        unregisterResize();
        DeleteBuffers();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    #endregion
}
