using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Adapts existing scratch-framebuffer callers to borrowed attachment instances.</summary>
public sealed partial class GpuFramebuffer
{
    #region Public API
    /// <summary>Borrows a 2D color image and selects its sole draw destination for scratch compatibility.</summary>
    public void Attach(DynamicTexture2D texture, int attachmentIndex = 0, int mipLevel = 0)
    {
        SetAttachment(ColorSlot(attachmentIndex), GpuFramebufferAttachment.FromTexture(texture, mipLevel));
        Bind();
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0 + attachmentIndex);
    }

    /// <summary>Borrows an external 2D color image and selects its draw destination.</summary>
    public void Attach(int textureId, int attachmentIndex = 0, int mipLevel = 0)
    {
        SetAttachment(ColorSlot(attachmentIndex), GpuFramebufferAttachment.FromTextureId(textureId, mipLevel));
        Bind();
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0 + attachmentIndex);
    }

    /// <summary>Borrows one 3D or array layer and selects it for scratch reads and writes.</summary>
    public void Attach(Texture3D texture, int layer, int attachmentIndex = 0)
    {
        SetAttachment(ColorSlot(attachmentIndex), GpuFramebufferAttachment.FromTexture(texture, layer: layer));
        Bind();
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0 + attachmentIndex);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0 + attachmentIndex);
    }

    /// <summary>Borrows a depth texture image and leaves this framebuffer bound for scratch operations.</summary>
    public void Attach(DepthTexture texture, int mipLevel = 0)
    {
        SetAttachment(FramebufferAttachment.DepthAttachment, GpuFramebufferAttachment.FromTexture(texture, mipLevel));
        Bind();
    }

    /// <summary>Borrows packed depth/stencil storage and leaves this framebuffer bound.</summary>
    public void Attach(DepthStencilTexture texture, int mipLevel = 0)
    {
        SetAttachment(FramebufferAttachment.DepthStencilAttachment, GpuFramebufferAttachment.FromTexture(texture, mipLevel));
        Bind();
    }

    /// <summary>Borrows stencil storage and leaves this framebuffer bound.</summary>
    public void Attach(StencilTexture texture, int mipLevel = 0)
    {
        SetAttachment(FramebufferAttachment.StencilAttachment, GpuFramebufferAttachment.FromTexture(texture, mipLevel));
        Bind();
    }

    /// <summary>Borrows a depth renderbuffer, validating an explicit packed depth/stencil request.</summary>
    public void Attach(GpuRenderbuffer renderbuffer, bool isDepthStencil = false)
    {
        var image = GpuFramebufferAttachment.FromRenderbuffer(renderbuffer);
        SetAttachment(isDepthStencil ? FramebufferAttachment.DepthStencilAttachment : image.DepthStencilSlot, image);
        Bind();
    }
    #endregion
}
