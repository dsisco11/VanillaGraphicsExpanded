using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Imports external framebuffer image metadata once at a publication boundary.</summary>
internal static class GpuFramebufferAttachmentDiscovery
{
    #region Public API
    /// <summary>Borrows a current attachment, preserving exact cube-face and individual-layer selection.</summary>
    public static GpuFramebufferAttachment Read(GpuFramebuffer framebuffer, FramebufferAttachment slot, bool forceNative = false)
    {
        var known = forceNative ? null : framebuffer.GetAttachment(slot);
        if (known is not null)
        {
            known.ValidateSlot(slot);
            return known;
        }
        if (framebuffer.IsDisposed || framebuffer.FboId == 0)
            throw new InvalidOperationException("An external FBO image is required.");
        using var bindings = StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, framebuffer.FboId);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, slot,
            FramebufferParameterName.FramebufferAttachmentObjectType, out int type);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, slot,
            FramebufferParameterName.FramebufferAttachmentObjectName, out int name);
        if (name == 0) throw new InvalidOperationException("The requested attachment is absent.");
        if (type == (int)All.Renderbuffer)
        {
            return GpuFramebufferAttachment.FromExternalImage(name, true, 0, 0, null);
        }
        if (type != (int)All.Texture) throw new InvalidOperationException("Unsupported framebuffer image type.");
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, slot,
            FramebufferParameterName.FramebufferAttachmentTextureLevel, out int level);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, slot,
            FramebufferParameterName.FramebufferAttachmentTextureCubeMapFace, out int face);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, slot,
            FramebufferParameterName.FramebufferAttachmentTextureLayer, out int layer);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, slot,
            FramebufferParameterName.FramebufferAttachmentLayered, out int layered);
        var texture = GpuResourceManager.AcquireExternalImageTexture(name);
        var target = texture.Target;
        // Layer zero is still a selected layer; querying the texture target distinguishes
        // it from an ordinary 2D image without probing invalid binding targets.
        bool hasLayers = target is TextureTarget.Texture1DArray or TextureTarget.Texture3D or TextureTarget.Texture2DArray
            or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray;
        return GpuFramebufferAttachment.FromExternalImage(name, false, face == 0 ? target : (TextureTarget)face, level,
            layer: layered == 0 && hasLayers ? layer : null, acquiredTexture: texture);
    }
    #endregion
}
