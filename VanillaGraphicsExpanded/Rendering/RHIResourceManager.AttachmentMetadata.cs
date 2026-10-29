using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Supplies selected attachment facts from canonical publications without readback or another metadata registry.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Requires a represented image selection while preserving whole-cube and view-relative projection policy.</summary>
    internal static GpuImageMetadata RequireTextureAttachmentMetadata(in GpuTextureMetadata data, in GpuImageSelection selection)
        => ProjectTextureImage(data, selection)
            ?? throw new InvalidOperationException("The selected framebuffer image has no published allocated storage.");
    #endregion
}
