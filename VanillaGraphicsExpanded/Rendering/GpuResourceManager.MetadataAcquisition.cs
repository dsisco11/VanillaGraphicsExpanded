using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires native facts and assigns snapshots without allowing wrappers to author metadata.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Checks original tracked identity and context before wrapper-based execution or ownership transfer.</summary>
    internal static void RequireWrapperMetadataAccess(GpuResource resource)
    {
        ObjectDisposedException.ThrowIf(resource.IsDisposed, resource);
        if (resource.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(resource))
            manager.RequireOriginalWrapper(resource);
    }

    /// <summary>Initializes an existing allocation through its canonical entry or a context-local native read.</summary>
    internal static void AcquireWrapperMetadata<T>(GpuResource<T> resource, IGpuResourceMetadataReader<T> reader) where T : struct
    {
        if (resource.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(resource))
        {
            manager.RequireOriginalWrapper(resource);
            // TODO(Rendering.MetadataLifecycle.todo, image acquisition task): replace this
            // borrower reader-selection path with maintained target-specific texture facts.
            // A generic image wrapper cannot represent a buffer texture. Reject before replacing
            // the shared target-specific reader or withdrawing a valid buffer-view snapshot.
            if (resource is GpuTexture && manager.FindResource(resource.Kind, resource.ResourceId)
                is GpuResourceMetadataEntry<GpuTextureMetadata> texture)
            {
                if (texture.Metadata.Target == RHI.TextureDimension.Buffer)
                    throw new NotSupportedException("Use GpuBufferView for buffer textures.");
                // The canonical entry already knows the immutable object target. Preserve that
                // fact when an external borrower conservatively refreshes through an older API.
                if (reader is GpuTextureMetadataReader && texture.Metadata.Target != 0)
                    reader = (IGpuResourceMetadataReader<T>)(object)new GpuTextureMetadataReader(texture.Metadata.Target);
            }
            var entry = manager.AcquireResource(resource.Kind, resource.ResourceId, reader)
                ?? throw new InvalidOperationException("Native resource metadata is unavailable.");
            if (!entry.ContainsRecipient(resource)) manager.AssociateResource(entry, resource);
            return;
        }
        RHICommandContext.RequireResourceOperationAvailable(resource.Kind, resource.ResourceId);
        if (resource.Kind is GpuResourceKind.Buffer or GpuResourceKind.MemoryObject)
            throw new InvalidOperationException("Maintained resource metadata requires its originating manager or supplied initialization snapshot.");
        // TODO(Rendering.MetadataLifecycle.todo, remaining family acquisition tasks): replace
        // implicit tool discovery with supplied initialization or explicitly recorded pre-install discovery.
        // Tools without an installed render service still receive facts from the same reader;
        // this creates no parallel registry or persistent tracking claim.
        T data;
        try
        {
            if (!reader.TryRead(resource.ResourceId, out data))
                throw new InvalidOperationException("Native resource metadata is unavailable.");
        }
        catch
        {
            DiscardUntrackedMetadata(resource);
            PublishOwnerImages(resource, false);
            throw;
        }
        resource.HandleMetadataChanged(data);
        PublishOwnerImages(resource, true);
    }

    /// <summary>Explicitly withdraws external facts before reacquiring them; available lookup never forces this path.</summary>
    internal static void RecoverWrapperMetadata<T>(GpuResource<T> resource, IGpuResourceMetadataReader<T> reader) where T : struct
    {
        // A context-local owned buffer can explicitly recover a failed storage operation
        // against its captured creation context. This is not raw-ID adoption, and available
        // supplied snapshots never need this read. Once a service is active, missing entries
        // remain tracking defects rather than permission to promote an unknown identity.
        if (resource.ResourceManager is null && resource is GpuBufferObject local
            && local.PublishedMetadata.Usage == RHI.BufferUsage.Undefined)
        {
            resource.RequireOriginalContext();
            ObjectDisposedException.ThrowIf(resource.IsDisposed, resource);
            RHICommandContext.RequireResourceOperationAvailable(resource.Kind, resource.ResourceId);
            if (!reader.TryRead(resource.ResourceId, out var recovered))
                throw new InvalidOperationException("Explicit context-local storage recovery failed.");
            resource.HandleMetadataChanged(recovered);
            return;
        }
        if (resource.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(resource))
        {
            manager.RequireOriginalWrapper(resource);
            var entry = manager.FindResource(resource.Kind, resource.ResourceId) as GpuResourceMetadataEntry<T>;
            // Reject incompatible borrowing before withdrawing another wrapper's valid snapshot.
            if (resource is GpuTexture && entry is GpuResourceMetadataEntry<GpuTextureMetadata> texture
                && texture.Metadata.Target == RHI.TextureDimension.Buffer)
                throw new NotSupportedException("Use GpuBufferView for buffer textures.");
            if (entry?.OperationInProgress == true)
                throw new InvalidOperationException("Metadata cannot be acquired during an unfinished update.");
            entry?.BeginRefresh(GpuMetadataChange.Metadata);
        }
        if (resource.Kind is GpuResourceKind.Buffer or GpuResourceKind.MemoryObject)
        {
            var ownerManager = resource.ResourceManager ?? throw new InvalidOperationException("Recovery requires an existing original registration.");
            var recovered = ownerManager.RecoverResource(resource.Kind, resource.ResourceId, reader)
                ?? throw new InvalidOperationException("Original resource recovery failed.");
            if (!recovered.ContainsRecipient(resource)) ownerManager.AssociateResource(recovered, resource);
        }
        // TODO(Rendering.MetadataLifecycle.todo, remaining family recovery tasks): replace
        // acquisition-based recovery with explicit recovery against the original registration.
        else AcquireWrapperMetadata(resource, reader);
    }

    #endregion

    #region Private
    /// <summary>Rejects retired aliases before lookup can discover or modify a reused numeric name.</summary>
    private void RequireOriginalWrapper(GpuResource resource)
    {
        ObjectDisposedException.ThrowIf(resource.IsDisposed, resource);
        if (GetResourceRegistration(resource) is { } original)
        {
            RequireLiveEntry(original);
            ValidateAssociation(original, resource);
        }
    }

    /// <summary>Allows new context-local objects after service retirement without reviving old associations.</summary>
    private bool CanAcquireWrapperMetadata(GpuResource resource)
    {
        if (Integration.RenderContextRegistry.Current() == default)
        {
            // A tracked wrapper cannot become a context-local object merely because its
            // originating namespace is no longer current. Reject before reads or dispatch.
            if (GetResourceRegistration(resource) is not null) RequireMetadataContext();
            return false;
        }
        if (!IsMetadataRegistryRetired)
        {
            // Available snapshots skip acquisition, but not native context/thread admission.
            // Every caller must pass this gate before withdrawing facts or issuing commands.
            RequireMetadataContext();
            return true;
        }
        // A retired service may still be installed while a replacement context creates objects.
        // TODO(Rendering.MetadataLifecycle.todo, remaining family acquisition tasks): remove
        // native-read fallback for new objects; retain rejection of terminal existing recipients.
        // Such new objects use native reads, but existing recipients retain their terminal identity.
        if (GetResourceRegistration(resource) is not null)
            throw new ObjectDisposedException(nameof(GpuResourceManager));
        return false;
    }
    #endregion
}
