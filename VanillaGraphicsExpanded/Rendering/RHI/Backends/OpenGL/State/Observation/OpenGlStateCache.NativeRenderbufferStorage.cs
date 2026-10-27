using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes external renderbuffer allocations through the common resource registry.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Resolves bound or explicit storage without admitting untracked native objects.</summary>
    internal static NativeRenderbufferStorage BeginNativeRenderbufferStorage(int selector, bool named)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true && Current.resourceOperation is null) return default;
        // Invalid targets must remain native errors, without withdrawing an unrelated object.
        if (!named && selector != (int)RenderbufferTarget.Renderbuffer) return default;
        int id = named ? selector : Current.GetCurrentRenderbuffer();
        Current.RequireResourceOperationAvailable(GpuResourceKind.Renderbuffer, id);
        if (manager?.CanObserveNativeMetadata != true) return default;
        var entry = manager.FindObservedResource(GpuResourceKind.Renderbuffer, id)
            as GpuResourceMetadataEntry<GpuRenderbufferMetadata>;
        if (entry is not null)
        {
            if (entry.OperationInProgress || entry.CompletionInProgress) throw new System.InvalidOperationException("A resource metadata operation is already in progress.");
            entry.OperationInProgress = true;
            entry.Reader ??= GpuRenderbufferMetadataReader.Instance;
            entry.BeginRefresh(GpuMetadataChange.Storage);
        }
        return new(entry);
    }
    #endregion

    /// <summary>Retains only the original entry while the native call defines its actual storage.</summary>
    internal readonly struct NativeRenderbufferStorage(GpuResourceMetadataEntry<GpuRenderbufferMetadata>? entry)
    {
        #region Internal API
        /// <summary>Publishes measured dimensions, format and samples to aliases and dependent images.</summary>
        internal void Complete(bool nativeReturned)
        {
            if (entry is not null) entry.OperationInProgress = false;
            if (entry is null || !entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
            if (!nativeReturned) { entry.NotifyImageDependents(suppressErrors: true); return; }
            // External calls retain their original request, including invalid or coverage
            // variants. All represented storage fields may change and require acquisition.
            if (!entry.Manager.RefreshMetadata(entry, GpuMetadataChange.Storage))
                throw new InvalidOperationException("Native renderbuffer metadata is unavailable after storage mutation.");
        }
        #endregion
    }
}
