using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes target metadata at resource boundaries rather than querying it during draws.</summary>
public sealed partial class GpuFramebuffer
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
    /// require a window-specific provider and remain unsupported by strict render passes.</remarks>
    public void PublishRenderPassMetadata()
    {
        RequireMutableStorage();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (ownsFramebuffer) throw new InvalidOperationException("Managed attachments already provide metadata.");
        if (fboId == 0) throw new NotSupportedException("Default framebuffer surface metadata is unavailable.");
        passMetadataPublished = false;
        using var binding = StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, fboId);
        // Build a complete candidate before replacing metadata; partial discovery must not escape.
        using var errors = new GlDebug.ErrorScope("Framebuffer metadata publication");
        using var source = Wrap(fboId, width: wrappedWidth, height: wrappedHeight);
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
        passMetadataPublished = true;
        PublishAttachmentsChanged();

    }
    #endregion

    #region Internal API
    /// <summary>Checks completeness after image or routing changes, retaining no draw-state values.</summary>
    internal void ValidatePassCompleteness(DrawBuffersEnum[] routing)
    {
        if (checkedPassRevision == AttachmentRevision && checkedPassRouting is not null
            && checkedPassRouting.AsSpan().SequenceEqual(routing)) return;
        if (!CheckStatus(out var error)) throw new InvalidOperationException(error);
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
        if (type != (int)All.None) candidate.Add(slot, GpuFramebufferAttachmentDiscovery.Read(source, slot));
    }
    #endregion
}
