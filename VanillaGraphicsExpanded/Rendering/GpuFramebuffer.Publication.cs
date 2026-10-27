using System;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Publishes external framebuffer identity through the admitted resource transaction.</summary>
public abstract partial class GpuFramebuffer
{
    private bool externalPublicationInProgress;
    #region Public API
    /// <summary>Refreshes an external identity and viewport without requiring complete pass metadata.</summary>
    public void RefreshWrappedFramebuffer(int existingFboId, int width, int height)
        => RefreshWrappedFramebuffer(existingFboId, width, height, false);
    /// <summary>Refreshes external identity and optionally publishes its complete selected-image set.</summary>
    public void RefreshWrappedFramebuffer(int existingFboId, int width, int height, bool publishPassMetadata)
        => RHI.RHICommandContext.Current.PublishImportedFramebuffer(this, publishPassMetadata, existingFboId, width, height);
    #endregion
    #region Internal API
    /// <summary>Validates owner policy and withdraws retained composition before an explicit external publication.</summary>
    internal bool BeginExternalPublication(int? replacementId, int width, int height)
    {
        if (externalPublicationInProgress) throw new InvalidOperationException("External publication is already in progress.");
        RequireMutableStorage(allowRetired: replacementId.HasValue);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (ownsFramebuffer) throw new InvalidOperationException("Only borrowed framebuffers can be refreshed.");
        if (!replacementId.HasValue && fboId == 0) throw new NotSupportedException("Default framebuffer surface metadata is unavailable.");
        bool replaces = replacementId.HasValue && (fboId != replacementId.Value || ResourceManager?.IsResourceLive(this) == false);
        if (replacementId.HasValue)
        {
            if (replacementId.Value <= 0 || width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(replacementId));
            if (replaces) RequireRetirement();
            Surface = null;
            attachments.Clear();
            attachmentsDirty = false;
            reconciledAttachmentDescriptors = null;
            wrappedWidth = width; wrappedHeight = height;
        }
        attachmentsDirty = false;
        reconciledAttachmentDescriptors = null;
        passMetadataPublished = false;
        externalPublicationInProgress = true;
        return replaces;
    }
    /// <summary>Commits pass readiness and emits the sole explicit publication notification, including on failure.</summary>
    internal void CompleteExternalPublication(bool available)
    {
        externalPublicationInProgress = false;
        passMetadataPublished = available;
        if (!available) attachments.Clear();
        PublishAttachmentsChanged();
    }
    #endregion
}
