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
        var discovered = framebuffer.DiscoverAttachment(slot, forceNative);
        return discovered;
    }
    #endregion
}
