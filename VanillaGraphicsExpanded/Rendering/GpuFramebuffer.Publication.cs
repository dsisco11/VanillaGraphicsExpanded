using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes rebuilt external framebuffer identities and attachment metadata.</summary>
public abstract partial class GpuFramebuffer
{
    #region Public API
    /// <summary>Refreshes external identity for compatibility callers without requiring strict pass metadata.</summary>
    public void RefreshWrappedFramebuffer(int existingFboId, int width, int height)
        => RefreshWrappedFramebuffer(existingFboId, width, height, publishPassMetadata: false);

    /// <summary>Refreshes external storage identity even when dimensions and integer GL names are unchanged.</summary>
    /// <param name="existingFboId">The positive framebuffer name published by the external owner after rebuilding.</param>
    /// <param name="width">The positive viewport width of the rebuilt framebuffer.</param>
    /// <param name="height">The positive viewport height of the rebuilt framebuffer.</param>
    /// <param name="publishPassMetadata">Resolves strict target metadata before publishing the single change notification.</param>
    /// <remarks>Clears cached attachment references and always notifies subscribers, including for equal-size rebuilds.</remarks>
    public void RefreshWrappedFramebuffer(int existingFboId, int width, int height, bool publishPassMetadata)
    {
        RequireMutableStorage();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (ownsFramebuffer) throw new InvalidOperationException("Only borrowed framebuffers can be refreshed.");
        if (existingFboId <= 0 || width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(existingFboId));
        StateCache.Current.InvalidateFramebufferDrawRouting(fboId);
        StateCache.Current.InvalidateFramebufferDrawRouting(existingFboId);
        Surface = null;
        attachments.Clear();
        attachmentsDirty = false;
        passMetadataPublished = false;
        fboId = existingFboId;
        wrappedWidth = width;
        wrappedHeight = height;
        if (!publishPassMetadata) PublishAttachmentsChanged();
        else
        {
            // Publication observes the completed image set once; failed discovery still
            // withdraws the old metadata and notifies existing borrowed-image consumers.
            try { PublishRenderPassMetadata(); }
            catch { PublishAttachmentsChanged(); throw; }
        }
    }

    #endregion
}
