using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Recovers affected framebuffer facts while retaining original completion ownership.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Reads one slot/default and preserves every unrelated descriptor under the completion guard.</summary>
    internal bool RefreshFramebufferField(GpuResourceMetadataEntry<GpuFramebufferMetadata> entry,
        FramebufferAttachment? slot, FramebufferDefaultParameter? parameter)
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        BeginMetadataCompletion(entry, GpuMetadataChange.Metadata);
        try
        {
            if (!GpuFramebufferMetadataReader.TryReadAffected(entry.Id, entry.Metadata, slot, parameter, out var candidate)) return false;
            candidate = ReconcileFramebufferAttachmentModes(entry, candidate);
            RequireMetadataContext();
            RequireLiveEntry(entry);
            RequireUninterruptedCompletion(entry);
            entry.Publish(candidate, GpuMetadataChange.Metadata | entry.PendingChange);
            return true;
        }
        finally { entry.CompletionInProgress = false; }
    }
    #endregion
}
