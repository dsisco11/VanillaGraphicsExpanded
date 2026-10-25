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
            // A generic image wrapper cannot represent a buffer texture. Reject before replacing
            // the shared target-specific reader or withdrawing a valid buffer-view snapshot.
            if (resource is GpuTexture && manager.FindResource(resource.Kind, resource.ResourceId)
                is GpuResourceMetadataEntry<GpuTextureMetadata> texture)
            {
                if (texture.Metadata.Target == OpenTK.Graphics.OpenGL.TextureTarget.TextureBuffer)
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
        StateCache.Current.RequireResourceOperationAvailable(resource.Kind, resource.ResourceId);
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
            throw;
        }
        resource.HandleMetadataChanged(data);
    }

    /// <summary>Explicitly withdraws external facts before reacquiring them; available lookup never forces this path.</summary>
    internal static void RecoverWrapperMetadata<T>(GpuResource<T> resource, IGpuResourceMetadataReader<T> reader) where T : struct
    {
        if (resource.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(resource))
        {
            manager.RequireOriginalWrapper(resource);
            var entry = manager.FindResource(resource.Kind, resource.ResourceId) as GpuResourceMetadataEntry<T>;
            // Reject incompatible borrowing before withdrawing another wrapper's valid snapshot.
            if (resource is GpuTexture && entry is GpuResourceMetadataEntry<GpuTextureMetadata> texture
                && texture.Metadata.Target == OpenTK.Graphics.OpenGL.TextureTarget.TextureBuffer)
                throw new NotSupportedException("Use GpuBufferView for buffer textures.");
            if (entry?.OperationInProgress == true)
                throw new InvalidOperationException("Metadata cannot be acquired during an unfinished update.");
            entry?.BeginRefresh(GpuMetadataChange.Metadata);
        }
        AcquireWrapperMetadata(resource, reader);
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
        // Such new objects use native reads, but existing recipients retain their terminal identity.
        if (GetResourceRegistration(resource) is not null)
            throw new ObjectDisposedException(nameof(GpuResourceManager));
        return false;
    }
    #endregion
}
