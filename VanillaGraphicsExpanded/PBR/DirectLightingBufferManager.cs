using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Manages GPU textures for the PBR direct lighting pass.
/// Creates and maintains framebuffers for:
/// - DirectDiffuseTex: diffuse BRDF contribution (RGBA16F)
/// - DirectSpecularTex: specular BRDF contribution (RGBA16F)
/// - EmissiveTex: emissive radiance (RGBA16F)
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

    // Screen dimensions tracking
    private int lastScreenWidth;
    private int lastScreenHeight;

    // Direct lighting output textures (all RGBA16F)
    private DynamicTexture2D? directDiffuseTex;
    private DynamicTexture2D? directSpecularTex;
    private DynamicTexture2D? emissiveTex;

    // Framebuffer for MRT output
    private GpuFramebuffer? directLightingFbo;

    private bool isInitialized;

    #endregion

    #region Properties

    /// <summary>
    /// Whether buffers have been created and are ready for use.
    /// </summary>
    public bool IsInitialized => isInitialized;

    /// <summary>
    /// Texture for direct diffuse radiance (RGBA16F).
    /// RGB = diffuse BRDF contribution, A = reserved.
    /// </summary>
    public DynamicTexture2D? DirectDiffuseTex => directDiffuseTex;

    /// <summary>
    /// OpenGL texture ID for direct diffuse.
    /// </summary>
    public int DirectDiffuseTextureId => directDiffuseTex?.TextureId ?? 0;

    /// <summary>
    /// Texture for direct specular radiance (RGBA16F).
    /// RGB = specular BRDF contribution, A = reserved.
    /// </summary>
    public DynamicTexture2D? DirectSpecularTex => directSpecularTex;

    /// <summary>
    /// OpenGL texture ID for direct specular.
    /// </summary>
    public int DirectSpecularTextureId => directSpecularTex?.TextureId ?? 0;

    /// <summary>
    /// Texture for emissive radiance (RGBA16F).
    /// RGB = emissive contribution, A = reserved.
    /// </summary>
    public DynamicTexture2D? EmissiveTex => emissiveTex;

    /// <summary>
    /// OpenGL texture ID for emissive.
    /// </summary>
    public int EmissiveTextureId => emissiveTex?.TextureId ?? 0;

    /// <summary>
    /// Framebuffer for direct lighting MRT output.
    /// Attachment0 = DirectDiffuse, Attachment1 = DirectSpecular, Attachment2 = Emissive.
    /// </summary>
    public GpuFramebuffer? DirectLightingFbo => directLightingFbo;

    #endregion

    #region Constructor / Destructor

    public DirectLightingBufferManager(ICoreClientAPI capi)
    {
        this.capi = capi;
        Instance = this;
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
        // Check if buffers need to be (re)created
        if (!isInitialized || screenWidth != lastScreenWidth || screenHeight != lastScreenHeight)
        {
            CreateBuffers(screenWidth, screenHeight);
            lastScreenWidth = screenWidth;
            lastScreenHeight = screenHeight;
        }

        return isInitialized && directLightingFbo != null && directLightingFbo.IsValid;
    }

    /// <summary>
    /// Binds the direct lighting FBO for MRT rendering.
    /// </summary>
    public void BindForRendering()
    {
        if (directLightingFbo == null || !directLightingFbo.IsValid)
        {
            capi.Logger.Warning("[VGE] DirectLightingBufferManager: FBO not valid for binding");
            return;
        }

        directLightingFbo.Bind();
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
        if (directLightingFbo == null || !directLightingFbo.IsValid)
            return;

        int prevFbo = GpuFramebuffer.SaveBinding();

        directLightingFbo.Bind();

        // Clear all color attachments to black
        float[] clearColor = [0f, 0f, 0f, 0f];
        GL.ClearBuffer(ClearBuffer.Color, 0, clearColor); // DirectDiffuse
        GL.ClearBuffer(ClearBuffer.Color, 1, clearColor); // DirectSpecular
        GL.ClearBuffer(ClearBuffer.Color, 2, clearColor); // Emissive

        GpuFramebuffer.RestoreBinding(prevFbo);
    }

    #endregion

    #region Private Methods

    private void CreateBuffers(int width, int height)
    {
        // NOTE: This method may run during rendering (e.g. on window resize).
        // Preserve the currently-bound framebuffer so we don't break the engine's render pipeline.
        int prevFbo = GpuFramebuffer.SaveBinding();

        try
        {
            // Delete old buffers if they exist
            DeleteBuffers();

        capi.Logger.Notification($"[VGE] Creating direct lighting buffers: {width}x{height}");

        // Create output textures (all RGBA16F for HDR)
        // Use linear filtering: these are screen-space radiance buffers that are sampled with
        // normalized UVs (e.g., LumOn ray-march hit sampling) rather than integer texelFetch.
        directDiffuseTex = DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear, debugName: "DirectDiffuse");
        directSpecularTex = DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear, debugName: "DirectSpecular");
        emissiveTex = DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear, debugName: "Emissive");

        // Validate texture creation
        if (directDiffuseTex == null || !directDiffuseTex.IsValid ||
            directSpecularTex == null || !directSpecularTex.IsValid ||
            emissiveTex == null || !emissiveTex.IsValid)
        {
            capi.Logger.Error("[VGE] Failed to create direct lighting textures");
            DeleteBuffers();
            return;
        }

        // Create MRT framebuffer
        directLightingFbo = GpuFramebuffer.CreateMRT(
            [directDiffuseTex, directSpecularTex, emissiveTex],
            depthTexture: null,
            ownsTextures: false,
            debugName: "DirectLightingFBO");

        if (directLightingFbo == null || !directLightingFbo.IsValid)
        {
            capi.Logger.Error("[VGE] Failed to create direct lighting FBO");
            DeleteBuffers();
            return;
        }

            // Verify FBO completeness
            directLightingFbo.Bind();
            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            GpuFramebuffer.Unbind();

            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                capi.Logger.Error($"[VGE] Direct lighting FBO incomplete: {status}");
                DeleteBuffers();
                return;
            }

            isInitialized = true;
        }
        finally
        {
            GpuFramebuffer.RestoreBinding(prevFbo);
        }
    }

    private void DeleteBuffers()
    {
        directLightingFbo?.Dispose();
        directLightingFbo = null;

        directDiffuseTex?.Dispose();
        directDiffuseTex = null;

        directSpecularTex?.Dispose();
        directSpecularTex = null;

        emissiveTex?.Dispose();
        emissiveTex = null;

        isInitialized = false;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        DeleteBuffers();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    #endregion
}
