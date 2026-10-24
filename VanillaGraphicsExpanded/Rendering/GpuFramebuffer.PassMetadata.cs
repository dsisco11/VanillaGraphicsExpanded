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
        using var binding = StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, fboId);
        // Build a complete candidate before replacing metadata; partial discovery must not escape.
        using var errors = new GlDebug.ErrorScope("Framebuffer metadata publication");
        using var source = DynamicGpuFramebuffer.Wrap(fboId, width: wrappedWidth, height: wrappedHeight);
        var candidate = new Dictionary<FramebufferAttachment, GpuFramebufferAttachment>();
        int count = Math.Min(16, GpuSupport.Graphics.MaxColorAttachments);
        for (int i = 0; i < count; i++) ReadPassAttachment(source, ColorSlot(i), candidate);
        ReadPassAttachment(source, FramebufferAttachment.DepthAttachment, candidate);
        ReadPassAttachment(source, FramebufferAttachment.StencilAttachment, candidate);
        GlDebug.ThrowIfErrors("Framebuffer metadata publication");
        attachments.Clear();
        foreach (var pair in candidate)
        {
            attachments.Add(pair.Key, pair.Value);
            pair.Value.Observe(this);
        }
        GpuResourceManager.AcquireWrapperMetadata(this, GpuFramebufferMetadataReader.Instance);
        passMetadataPublished = true;
        PublishAttachmentsChanged();

    }
    #endregion

    #region Protected API
    /// <summary>Checks the already-selected draw target after image or routing changes without disturbing the read binding.</summary>
    private protected void ValidatePassCompletenessCore(IReadOnlyList<DrawBuffersEnum> routing)
    {
        if (StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer) != fboId)
            throw new InvalidOperationException("Pass completeness requires this framebuffer's draw binding.");
        if (checkedPassRevision == AttachmentRevision && checkedPassRouting is not null
            && checkedPassRouting.SequenceEqual(routing)) return;
        // Pass setup already refreshed and bound the images. A general-purpose CheckStatus scope
        // would needlessly select the read alias and restore it before every changed output route.
        var status = GL.CheckFramebufferStatus(FramebufferTarget.DrawFramebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"Framebuffer incomplete: {status}");
        checkedPassRevision = AttachmentRevision;
        checkedPassRouting = routing.ToArray();
    }
    #endregion

    #region Private
    /// <summary>Uses an empty borrowed wrapper so discovery cannot reuse a prior publication's stale images.</summary>
    private static void ReadPassAttachment(GpuFramebuffer source, FramebufferAttachment slot,
        Dictionary<FramebufferAttachment, GpuFramebufferAttachment> candidate)
    {
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, slot,
            FramebufferParameterName.FramebufferAttachmentObjectType, out int type);
        if (type != (int)All.None) candidate.Add(slot, GpuFramebufferAttachmentDiscovery.Read(source, slot, forceNative: true));
    }
    #endregion
}
