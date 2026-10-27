using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Composes canonical framebuffer attachment facts without native execution.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Constructs canonical selections centrally, including packed depth/stencil expansion.</summary>
    internal static GpuFramebufferMetadata WithFramebufferAttachment(GpuFramebufferMetadata candidate,
        FramebufferAttachment slot, GpuFramebufferAttachment? image)
    {
        if (slot == FramebufferAttachment.DepthStencilAttachment)
            return WithFramebufferAttachment(WithFramebufferAttachment(candidate, FramebufferAttachment.DepthAttachment, image), FramebufferAttachment.StencilAttachment, image);
        var selections = candidate.Attachments;
        if (image is null) return candidate with { Attachments = selections.Remove(slot) };
        var descriptor = new GpuFramebufferImageMetadata(image.Kind, image.ResourceId, image.MipLevel,
            image.Layer ?? 0, image.CubeFace ?? 0, image.MetadataImageSelection.Layered,
            image.BaseView, image.ViewCount, image.AttachmentMode);
        return candidate with { Attachments = selections.SetItem(slot, descriptor) };
    }
    #endregion
}
