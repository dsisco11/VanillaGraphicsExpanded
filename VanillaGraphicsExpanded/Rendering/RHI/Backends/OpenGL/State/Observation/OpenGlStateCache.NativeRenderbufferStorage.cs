using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes external renderbuffer allocations through the common resource registry.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles a completed native mutation and releases metadata bookkeeping on every exit.</summary>
    internal static void ObserveRenderbufferStorage(int selector, bool named)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true && Current.resourceOperation is null) return;
        // Invalid targets must remain native errors, without withdrawing an unrelated object.
        if (!named && selector != (int)RenderbufferTarget.Renderbuffer) return;
        int id = named ? selector : Current.GetCurrentRenderbuffer();
        Current.InterruptNativeResourceOperation(GpuResourceKind.Renderbuffer, id);
        if (manager?.CanObserveNativeMetadata != true) return;
        var entry = manager.FindObservedResource(GpuResourceKind.Renderbuffer, id)
            as GpuResourceMetadataEntry<GpuRenderbufferMetadata>;
        if (entry is not null)
        {
            if (InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage)) return;
            entry.OperationInProgress = true;
            entry.Reader ??= GpuRenderbufferMetadataReader.Instance;
            entry.BeginRefresh(GpuMetadataChange.Storage);
        }
        try
        {
            if (entry is not null) entry.OperationInProgress = false;
            if (entry is null || !entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;

            // External calls retain their original request, including invalid or coverage
            // variants. All represented storage fields may change and require acquisition.
            if (!entry.Manager.RefreshMetadata(entry, GpuMetadataChange.Storage))
                throw new InvalidOperationException("Native renderbuffer metadata is unavailable after storage mutation.");
        }
        finally { if (entry is not null) entry.OperationInProgress = false; }
    }
    #endregion
}
