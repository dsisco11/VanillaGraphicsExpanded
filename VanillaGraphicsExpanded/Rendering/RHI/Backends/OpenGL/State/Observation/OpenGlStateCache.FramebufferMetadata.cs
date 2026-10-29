using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Composes canonical framebuffer attachment facts without native execution.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Private
    /// <summary>Constructs canonical selections centrally, including packed depth/stencil expansion.</summary>
    internal static GpuFramebufferMetadata WithFramebufferAttachment(GpuFramebufferMetadata candidate,
        FramebufferSlot slot, GpuFramebufferAttachment? image)
    {
        if (slot == FramebufferSlot.DepthStencil)
            return WithFramebufferAttachment(WithFramebufferAttachment(candidate, FramebufferSlot.Depth, image), FramebufferSlot.Stencil, image);
        var selections = candidate.Attachments;
        if (image is null) return candidate with { Attachments = selections.Remove(slot) };
        var descriptor = new GpuFramebufferImageMetadata(image.Kind, image.ResourceId, image.MipLevel,
            image.Layer ?? 0, image.CubeFace ?? 0, image.MetadataImageSelection.Layered,
            image.BaseView, image.ViewCount, image.AttachmentMode)
            { OriginalBacking = image.ResourceManager?.GetResourceRegistration(image) };
        return candidate with { Attachments = selections.SetItem(slot, descriptor) };
    }
    #endregion
}
