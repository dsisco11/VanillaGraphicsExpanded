using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

internal sealed class HzbTestPyramid : IDisposable
{
    private readonly int _fbo;
    private bool _isDisposed;

    public HzbTestPyramid(int width, int height, int mipLevels)
    {
        Texture = DynamicTexture2D.CreateMipmapped(width, height, PixelInternalFormat.R32f, mipLevels);
        _fbo = GL.GenFramebuffer();
    }

    public DynamicTexture2D Texture { get; }

    public int MipLevels => Texture.MipLevels;

    public void BindMipForWrite(int mipLevel)
    {
        mipLevel = Math.Clamp(mipLevel, 0, Math.Max(0, Texture.MipLevels - 1));

        int mipWidth = Math.Max(1, Texture.Width >> mipLevel);
        int mipHeight = Math.Max(1, Texture.Height >> mipLevel);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            Texture.TextureId,
            mipLevel);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        GL.Viewport(0, 0, mipWidth, mipHeight);
    }

    public void Unbind()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        Texture.Dispose();
        GL.DeleteFramebuffer(_fbo);
    }
}
