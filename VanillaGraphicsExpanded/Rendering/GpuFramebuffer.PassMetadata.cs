using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes target metadata at resource boundaries rather than querying it during draws.</summary>
public abstract partial class GpuFramebuffer
{
    internal ulong AttachmentRevision { get; private set; }
    private bool passMetadataPublished;
    internal bool HasRenderPassMetadata => ownsFramebuffer || passMetadataPublished;
    internal IEnumerable<KeyValuePair<FramebufferSlot, GpuFramebufferAttachment>> AttachmentImages => attachments;

    #region Public API
    /// <summary>Captures all external attachment metadata after framebuffer publication or refresh.</summary>
    /// <remarks>The external owner must call this again after changing native storage. Default surfaces
    /// use the separate window-surface metadata publication entry point.</remarks>
    public void PublishRenderPassMetadata()
    {
        RHI.RHICommandContext.Current.PublishImportedFramebuffer(this, true);
    }
    #endregion

}
