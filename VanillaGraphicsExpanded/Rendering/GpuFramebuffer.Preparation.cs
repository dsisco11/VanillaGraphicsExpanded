using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains attachment readiness for owner and engine callback selection until relevant publication.</summary>
public abstract partial class GpuFramebuffer
{
    private AttachmentPreparation? attachmentPreparation;

    #region Internal API
    /// <summary>Establishes selected attachment readiness once; failed preparation remains withdrawn until change delivery.</summary>
    internal void PrepareAttachments()
    {
        if (attachmentPreparation is null)
        {
            attachmentPreparation = new();
            ObservePreparation(attachmentPreparation, attachmentPreparation.Invalidate);
        }
        var prepared = attachmentPreparation;
        if (prepared.Dirty)
        {
            prepared.Dirty = false;
            prepared.Failure = null;
            try { ValidateAttachments(); }
            catch (Exception error) { prepared.Failure = error; }
        }
        if (prepared.Failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(prepared.Failure).Throw();
    }
    #endregion

    #region Private
    /// <summary>Keeps a weak invalidation key separate from framebuffer-specific native realization owners.</summary>
    private sealed class AttachmentPreparation
    {
        internal bool Dirty = true;
        internal Exception? Failure;
        /// <summary>Withdraws only cached readiness; the next explicit preparation establishes new facts.</summary>
        internal void Invalidate() { Dirty = true; }
    }
    #endregion
}
