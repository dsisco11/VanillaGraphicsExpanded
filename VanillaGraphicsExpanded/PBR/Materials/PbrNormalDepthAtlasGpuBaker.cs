using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal static class PbrNormalDepthAtlasGpuBaker
{
    /// <summary>
    /// GPU-side bake entrypoint for normal+depth sidecar atlas pages.
    /// Reads from the base albedo atlas page and writes into the destination sidecar texture.
    /// </summary>
    /// <remarks>
    /// @todo Replace placeholder output with real depth-from-albedo and normals-from-depth algorithm.
    /// </remarks>
    public static void Bake(
        int baseAlbedoAtlasPageTexId,
        int destNormalDepthTexId,
        int width,
        int height)
    {
        if (baseAlbedoAtlasPageTexId == 0) throw new ArgumentOutOfRangeException(nameof(baseAlbedoAtlasPageTexId));
        if (destNormalDepthTexId == 0) throw new ArgumentOutOfRangeException(nameof(destNormalDepthTexId));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        // Placeholder output (Phase 3): constant normal (0.5, 0.5, 1.0) in RGB and depth=0 in A.
        // This establishes the plumbing without committing to any algorithm.
        int[] prevViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, prevViewport);

        GL.GetInteger(GetPName.FramebufferBinding, out int prevFbo);

        int fbo = 0;
        try
        {
            fbo = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);

            GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                destNormalDepthTexId,
                level: 0);

            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                return;
            }

            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.Viewport(0, 0, width, height);

            GL.ClearColor(0.5f, 0.5f, 1.0f, 0.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }
        catch
        {
            // Swallow errors during early init/shutdown; the binding hook will no-op if the texture is missing.
        }
        finally
        {
            if (fbo != 0)
            {
                GL.DeleteFramebuffer(fbo);
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
            GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);
        }
    }
}
