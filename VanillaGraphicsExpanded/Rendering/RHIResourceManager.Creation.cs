using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Commits synchronous owned creation without exposing a provisional native handle to callers.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Creates, associates and delivers one complete snapshot before exposing success.</summary>
    /// <remarks>Resource-family facades provide value backends; this path requires no per-call delegates.</remarks>
    internal static TMetadata? CreateResource<TInitialization, TMetadata, TCreation>(in TInitialization initialization,
        GpuResource<TMetadata> recipient, out GpuResourceHandle handle, TCreation creation = default)
        where TInitialization : struct where TMetadata : struct
        where TCreation : struct, IGpuResourceCreation<TInitialization, TMetadata>
    {
        handle = default;
        ArgumentNullException.ThrowIfNull(recipient);
        ObjectDisposedException.ThrowIf(recipient.IsDisposed, recipient);
        var manager = recipient.ResourceManager;
        if (recipient.ResourceId != 0 || recipient.Kind != creation.Kind
            || manager?.GetResourceRegistration(recipient) is not null)
            throw new InvalidOperationException("Creation requires an unpublished declaration of the requested kind.");
        // Freeze value intent before validation; family backends also freeze referenced inputs.
        var request = initialization;
        creation.Validate(request);
        var origin = RenderContextRegistry.Current();
        var nativeOrigin = GpuResource.CaptureCreationContext(creation.Kind);
        if (manager is not null && manager.CanAcquireWrapperMetadata(recipient)) manager.RequireMetadataContext();
        else manager = null;
        nint id = 0;
        GpuResourceMetadataEntry<TMetadata>? entry = null;
        bool committed = false;
        try
        {
            // The backend assigns id immediately upon allocation, so every reported failure
            // follows the same ownership cleanup even before canonical registration exists.
            var candidate = creation.Initialize(RHI.RHICommandContext.Current, request, out id);
            if (!candidate.HasValue || id == 0) return null;
            if (origin != RenderContextRegistry.Current() || !VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(nativeOrigin))
                throw new InvalidOperationException("The creation context changed before publication.");
            ValidateMetadataType<TMetadata>(creation.Kind, id);
            if (manager is not null)
            {
                manager.RequireMetadataContext();
                if (manager.FindResource(creation.Kind, id) is not null)
                    throw new InvalidOperationException("Creation cannot replace an existing registration.");
                entry = manager.RegisterCreatedResource<TMetadata>(creation.Kind, id);
                // Publish before associating the still-unpublished wrapper. No dependent callbacks
                // can run on it during construction, and association delivers the snapshot once.
                // Semaphore generation allocates a name only. Its import outcome establishes
                // realization later; the owner can still dispose an unimported reservation.
                if (creation.Kind != GpuResourceKind.Semaphore)
                    manager.PublishMetadata(entry, candidate.Value, GpuMetadataChange.None);
                candidate = entry.Metadata;
            }
            recipient.SetCreationIdentity(id, nativeOrigin);
            if (!VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(nativeOrigin) || origin != RenderContextRegistry.Current()
                || origin != default && !RenderContextRegistry.IsAlive(origin))
                throw new InvalidOperationException("The creation context retired during identity handoff.");
            if (manager is not null)
            {
                manager.AssociateResource(entry!, recipient);
                manager.RequireMetadataContext();
                manager.RequireLiveEntry(entry!);
                if (creation.Kind != GpuResourceKind.Semaphore && !manager.IsResourceMetadataAvailable(recipient))
                    throw new InvalidOperationException("Initial metadata delivery did not complete.");
            }
            else recipient.AssignMetadataSnapshot(candidate.Value);
            handle = new(creation.Kind, id);
            committed = true;
            return candidate;
        }
        finally
        {
            if (!committed)
            {
                // Cleanup cannot replace the initialization exception. Entry identity protects
                // against reentrant deletion/name reuse; foreign contexts defer native retirement.
                if (entry is not null)
                {
                    entry.Unregister(recipient);
                    manager!.resourceAssociations.Remove(recipient);
                }
                if (recipient.ResourceId == id && id != 0) recipient.SetCreationIdentity(0);
                try
                {
                    if (id != 0 && (entry is not null ? manager!.IsRegistered(entry)
                        : manager is null || manager.FindResource(creation.Kind, id) is null))
                        GpuResource.RetireResource(creation.Kind, id, nativeOrigin, manager);
                }
                catch { }
                finally
                {
                    // Existing queued deletion retains original map identity until its native
                    // context can drain. The tombstone has no recipient or usable metadata.
                    if (entry is not null && entry.State != GpuResourceRegistrationState.Retiring)
                    {
                        manager!.metadataRegistry.TryRemove(new KeyValuePair<GpuResourceHandle, GpuResourceRegistration>(entry.Handle, entry));
                        entry.Invalidate(GpuResourceRegistrationState.Deleted);
                    }
                }
            }
        }
    }
    #endregion
}
