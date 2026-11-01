using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Copies native fixture attachments, including renderbuffers, into independently owned GPU assertion images.</summary>
internal static class NativeFramebufferImage
{
    #region Internal API
    /// <summary>Retains the complete selected color attachment while restoring source routing, framebuffer bindings, scissor and rasterizer discard.</summary>
    internal static GpuImageSnapshot Capture(int framebuffer,int attachment,int width,int height,TextureFormat format)
    {
        var cache=OpenGlStateCache.Current;
        using var bindings=cache.BindFramebufferScope();
        using var image=new GpuFramebufferAttachment(width,height,format);
        using var target=DynamicGpuFramebuffer.Create([image])!;
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,framebuffer);
        GL.GetInteger(GetPName.ReadBuffer,out int previousRead);
        bool scissor=GL.IsEnabled(EnableCap.ScissorTest);
        bool discard=GL.IsEnabled(EnableCap.RasterizerDiscard);
        try
        {
            // Native reference draws intentionally do not publish an RHI framebuffer owner.
            // A GPU blit preserves those independent subjects without downloading their pixels.
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0+attachment);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,target.FboId);
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.RasterizerDiscard);
            GL.BlitFramebuffer(0,0,width,height,0,0,width,height,ClearBufferMask.ColorBufferBit,BlitFramebufferFilter.Nearest);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
            return new((GpuTexture)image.Resource!);
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,framebuffer);
            GL.ReadBuffer((ReadBufferMode)previousRead);
            if(scissor)GL.Enable(EnableCap.ScissorTest);else GL.Disable(EnableCap.ScissorTest);
            if(discard)GL.Enable(EnableCap.RasterizerDiscard);else GL.Disable(EnableCap.RasterizerDiscard);
        }
    }
    #endregion
}
