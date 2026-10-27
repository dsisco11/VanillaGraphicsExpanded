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
    internal IEnumerable<KeyValuePair<FramebufferAttachment, GpuFramebufferAttachment>> AttachmentImages => attachments;
    private ulong? checkedPassRevision;
    private DrawBuffersEnum[]? checkedPassRouting;

    #region Public API
    /// <summary>Captures all external attachment metadata after framebuffer publication or refresh.</summary>
    /// <remarks>The external owner must call this again after changing native storage. Default surfaces
    /// use the separate window-surface metadata publication entry point.</remarks>
    public void PublishRenderPassMetadata()
    {
        RequireMutableStorage();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (ownsFramebuffer) throw new InvalidOperationException("Managed attachments already provide metadata.");
        if (fboId == 0) throw new NotSupportedException("Default framebuffer surface metadata is unavailable.");
        StateCache.Current.InvalidateFramebufferDrawRouting(fboId);
        passMetadataPublished = false;
        // One manager-owned discovery supplies both canonical descriptors and image reconciliation.
        // Do not scan slots independently before asking the canonical reader for the same facts.
        try
        {
            GpuResourceManager.RecoverWrapperMetadata(this, GpuFramebufferMetadataReader.Instance);
            ReconcileAttachmentMetadata(allowUnpublishedExternal: true, recoverBackings: true);
        }
        catch
        {
            GpuResourceManager.InvalidateWrapperMetadata(this);
            GpuResourceManager.DiscardUntrackedMetadata(this);
            throw;
        }
        passMetadataPublished = true;
        PublishAttachmentsChanged();

    }
    #endregion

    #region Internal API
    /// <summary>Checks the already-selected draw target after image or routing changes without disturbing the read binding.</summary>
    internal void RequirePassCompleteness(IReadOnlyList<DrawBuffersEnum> routing)
    {
        if(this is RHI.Backends.OpenGL.OpenGLPassTarget realized)
        {
            if(!realized.IsReusable)throw new InvalidOperationException("The backend target realization is obsolete.");
            return;
        }
        if(fboId==0 && Surface is not null)return;
        if (StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer) != fboId)
            throw new InvalidOperationException("Pass completeness requires this framebuffer's draw binding.");
        if (checkedPassRevision == AttachmentRevision && checkedPassRouting is not null
            && checkedPassRouting.SequenceEqual(routing)) return;
        // Pass setup already refreshed and bound the images. A general-purpose CheckStatus scope
        // would needlessly select the read alias and restore it before every changed output route.
        RHI.Backends.OpenGL.OpenGLFramebufferCompleteness.RequireComplete(fboId);
        checkedPassRevision = AttachmentRevision;
        checkedPassRouting = routing.ToArray();
    }
    #endregion

}
