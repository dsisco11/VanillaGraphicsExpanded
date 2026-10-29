using System;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Applies native ownership and shared usage rules through manager-owned wrapper associations.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    #region Native validity and shared guards
    /// <summary>Rejects retired original objects while preserving unregistered declaration behavior.</summary>
    internal bool IsResourceLive(GpuResource resource)
    {
        return GetResourceRegistration(resource) is not { } entry || IsRegistered(entry)
            && entry.State == GpuResourceRegistrationState.Live;
    }

    /// <summary>Applies existing storage guards across aliases of the same native object.</summary>
    internal void RequireResourceMutable(GpuResource resource, bool allowRetired)
    {
        ObjectDisposedException.ThrowIf(!allowRetired && !IsResourceLive(resource), resource);
        GetResourceRegistration(resource)?.RequireMutableStorage();
    }

    /// <summary>Applies existing retirement guards across aliases of the same native object.</summary>
    internal void RequireResourceRetirement(GpuResource resource)
    {
        GetResourceRegistration(resource)?.RequireRetirement();
    }

    /// <summary>Retains an already associated native object for a pass or restoration boundary.</summary>
    internal void RetainResource(GpuResource resource, bool pass)
    {
        GetResourceRegistration(resource)?.Retain(pass);
    }

    /// <summary>Releases shared guard state even when the original entry has since retired.</summary>
    internal void ReleaseResource(GpuResource resource, bool pass)
    {
        GetResourceRegistration(resource)?.Release(pass);
    }
    #endregion

    #region Wrapper publication and removal
    /// <summary>Associates a generated native name without inventing initialized metadata.</summary>
    internal void RegisterWrapperDeclaration<TMetadata>(GpuResource<TMetadata> resource) where TMetadata : struct
    {
        if (resource.ResourceId == 0 || IsMetadataRegistryRetired || RenderContextRegistry.Current() == default) return;
        var entry = FindResource(resource.Kind, resource.ResourceId) as GpuResourceMetadataEntry<TMetadata>;
        if (entry is null && UsesMaintainedLifecycle(resource.Kind))
            throw new InvalidOperationException("A wrapper declaration cannot manufacture a missing observed resource lifetime.");
        entry ??= RegisterCreatedResource<TMetadata>(resource.Kind, resource.ResourceId);
        if (!ReferenceEquals(GetResourceRegistration(resource), entry)) AssociateResource(entry, resource);
    }

    /// <summary>Removes delivery membership while preserving original identity for stale-owner checks.</summary>
    internal void UnregisterWrapper(GpuResource resource)
    {
        GetResourceRegistration(resource)?.Unregister(resource);
    }

    /// <summary>Ends an intentional owner replacement before its next native object is registered.</summary>
    internal void ReplaceWrapperResource(GpuResource resource)
    {
        UnregisterWrapper(resource);
        resourceAssociations.Remove(resource);
    }
    #endregion

    #region Owning retirement
    /// <summary>Captures original registration identity before native deletion can be deferred or a name reused.</summary>
    internal bool TryScheduleResourceRetirement(GpuResource resource, nint id, (nint Handle, long Generation) origin)
    {
        if (GetResourceRegistration(resource) is not { } entry) return false;
        if (!metadataRegistry.TryGetValue(entry.Handle, out var current) || !ReferenceEquals(current, entry)
            || entry.State != GpuResourceRegistrationState.Live) return true;
        RetireResource(entry);
        // Keep the same context policy as legacy owner disposal, but carry the original entry
        // rather than resolving a potentially reused numeric name when the queue drains.
        var command = new GpuDeletionCommand(resource.Kind, id, origin, Registration: entry);
        if (IsDisposed)
        {
            // Admission is closed, but the live-context disposal drain still owns these entries.
            // Only its captured original cleanup can enter this terminal native queue.
            if (CanDrainNative)
            {
                deletionQueue.Enqueue(command);
                DrainDeletionQueue();
            }
            return true;
        }
        EnqueueDeletion(command);
        if (CanDrainNative) DrainDeletionQueue();
        return true;
    }
    #endregion
    #endregion
}
