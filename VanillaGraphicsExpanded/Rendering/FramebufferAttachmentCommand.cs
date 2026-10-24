using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains command admission evidence separately from queryable native attachment descriptors.</summary>
internal readonly record struct FramebufferAttachmentCommand(FramebufferAttachment Slot, int Texture, int Level,
    int Layer, int BaseView, int ViewCount, GpuFramebufferAttachmentMode Mode, bool Eligible, bool Rejected)
{
    #region Internal API
    /// <summary>Requires the resulting queried selection to agree before assigning a command's mode.</summary>
    internal bool Matches(GpuFramebufferImageMetadata image)
    {
        if (image.Kind != GpuResourceKind.Texture || image.Id != Texture || image.MipLevel != Level) return false;
        return Mode == GpuFramebufferAttachmentMode.Multiview
            ? image.BaseView == BaseView && image.ViewCount == ViewCount
            : Layer < 0 ? image.Layered : !image.Layered && image.Layer == Layer;
    }
    #endregion
}
