using System;

using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Renderer that attaches the G-buffer to the Primary framebuffer before chunk rendering.
/// Registered at EnumRenderStage.Before to ensure MRT is active when chunks draw.
/// </summary>
public sealed class GBufferRenderer : IRenderer, IDisposable
{
    private readonly ICoreClientAPI capi;
    private int normalTextureId;
    private int lastWidth;
    private int lastHeight;
    private bool isInitialized;
    private bool shaderInjectionAttempted;

    /// <summary>
    /// The OpenGL texture ID for the normal G-buffer.
    /// </summary>
    public int NormalTextureId => normalTextureId;

    public double RenderOrder => 0.0; // Very early
    public int RenderRange => 1;

    public GBufferRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        
        // Register at Before stage to set up MRT before chunks render
        capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "gbuffer-setup");
        
        // Hook into shader reload to inject normal output
        capi.Event.ReloadShader += OnReloadShaders;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        int width = primaryFb.Width;
        int height = primaryFb.Height;

        // Check if we need to (re)create the texture due to size change
        if (!isInitialized || width != lastWidth || height != lastHeight)
        {
            CreateNormalTexture(width, height);
            AttachToFramebuffer(primaryFb.FboId);
            lastWidth = width;
            lastHeight = height;
        }

        // Ensure MRT is active each frame (in case something reset it)
        EnsureMRTActive(primaryFb.FboId);
    }

    private void CreateNormalTexture(int width, int height)
    {
        // Delete old texture if exists
        if (normalTextureId != 0)
        {
            GL.DeleteTexture(normalTextureId);
            normalTextureId = 0;
        }

        // Generate new texture
        normalTextureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, normalTextureId);

        // Allocate storage - Rgba16f for high precision normals
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba16f,
            width,
            height,
            0,
            PixelFormat.Rgba,
            PixelType.Float,
            IntPtr.Zero);

        // Set filtering (nearest for G-buffer data)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindTexture(TextureTarget.Texture2D, 0);

        isInitialized = true;
        capi.Logger.Notification($"[VGE] Created G-buffer normal texture: {width}x{height}, ID={normalTextureId}");
    }

    private void AttachToFramebuffer(int fboId)
    {
        // Bind the Primary framebuffer
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

        // Attach our normal texture as ColorAttachment1
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment1,
            TextureTarget.Texture2D,
            normalTextureId,
            0);

        // Verify framebuffer is complete
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            capi.Logger.Error($"[VGE] Framebuffer incomplete after attaching G-buffer: {status}");
        }
        else
        {
            capi.Logger.Notification("[VGE] G-buffer normal texture attached to Primary framebuffer");
        }

        // Unbind
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void EnsureMRTActive(int fboId)
    {
        // Bind and set draw buffers each frame
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
        
        DrawBuffersEnum[] drawBuffers = { 
            DrawBuffersEnum.ColorAttachment0, 
            DrawBuffersEnum.ColorAttachment1 
        };
        GL.DrawBuffers(2, drawBuffers);
        
        // Don't unbind - leave it bound for subsequent rendering
    }

    private bool OnReloadShaders()
    {
        // Try to inject normal output into chunk shaders
        TryInjectChunkShaderNormalOutput();
        return true;
    }

    private void TryInjectChunkShaderNormalOutput()
    {
        if (shaderInjectionAttempted)
        {
            return; // Only try once per session to avoid log spam
        }
        shaderInjectionAttempted = true;

        // Code to inject into the fragment shader
        // This declares a second output for MRT and outputs the normal
        const string normalOutputCode = @"
// VGE G-Buffer normal output
layout(location = 1) out vec4 vge_outNormal;
#define VGE_GBUFFER_ENABLED 1
";

        // Try various chunk shader names
        string[] shaderNames = { "chunkopaque", "chunkliquid", "chunktopsoil" };

        foreach (var shaderName in shaderNames)
        {
            try
            {
                var shader = capi.Shader.GetProgramByName(shaderName);
                if (shader?.FragmentShader == null)
                {
                    capi.Logger.Debug($"[VGE] Shader '{shaderName}' not found or has no fragment shader");
                    continue;
                }

                // Try to set PrefixCode
                string existingPrefix = shader.FragmentShader.PrefixCode ?? "";
                if (!existingPrefix.Contains("VGE_GBUFFER_ENABLED"))
                {
                    shader.FragmentShader.PrefixCode = normalOutputCode + existingPrefix;
                    capi.Logger.Notification($"[VGE] Injected G-buffer output into '{shaderName}' fragment shader via PrefixCode");
                    
                    // Note: This may not take effect until shaders are recompiled
                    // The game may need shader.Compile() called, but that might cause issues
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Warning($"[VGE] Failed to inject into '{shaderName}': {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        capi.Event.ReloadShader -= OnReloadShaders;

        // Detach from framebuffer
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb != null)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);

            // Detach our texture
            GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment1,
                TextureTarget.Texture2D,
                0,
                0);

            // Reset draw buffers to only ColorAttachment0
            DrawBuffersEnum[] drawBuffers = { DrawBuffersEnum.ColorAttachment0 };
            GL.DrawBuffers(1, drawBuffers);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        if (normalTextureId != 0)
        {
            GL.DeleteTexture(normalTextureId);
            normalTextureId = 0;
        }

        capi.Logger.Notification("[VGE] G-buffer renderer disposed");
    }
}