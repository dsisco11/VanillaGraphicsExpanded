using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes StateCache command results to canonical entries and their synchronized wrapper snapshots.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Discards uncertified local facts while retaining the established texture target for native recovery.</summary>
    internal static void DiscardUntrackedMetadata<T>(GpuResource<T> resource,
        GpuMetadataChange change = GpuMetadataChange.Metadata) where T : struct
    {
        if (resource.ResourceManager?.GetResourceRegistration(resource) is not null) return;
        if ((change & GpuMetadataChange.Storage) != 0 && resource is GpuTexture changedTexture)
            changedTexture.UntrackedStorageIdentity = new();
        // There is no availability registry in context-local tools. Empty descriptors cannot
        // masquerade as the previous allocation; object target alone remains a stable native fact.
        if (resource is GpuResource<GpuTextureMetadata> texture)
        {
            var target = resource is GpuTexture owner ? owner.TextureTarget
                : resource is GpuTextureView view ? view.Target
                : resource is GpuBufferView ? OpenTK.Graphics.OpenGL.TextureTarget.TextureBuffer : 0;
            texture.HandleMetadataChanged(default(GpuTextureMetadata) with { Target = target });
        }
        else resource.HandleMetadataChanged(default);
    }

    /// <summary>Withdraws usability when an acquisition or a grouped native command cannot certify a complete snapshot.</summary>
    internal static void InvalidateWrapperMetadata<T>(GpuResource<T> resource,
        GpuMetadataChange change = GpuMetadataChange.Metadata) where T : struct
    {
        if (resource.ResourceManager?.GetResourceRegistration(resource) is GpuResourceMetadataEntry<T> entry)
        {
            entry.BeginRefresh(change);
            entry.NotifyProgramDependents(change, available: false);
        }
    }

    /// <summary>Withdraws an unfinished command group without reading or publishing its partial native state.</summary>
    internal static void FailWrapperMetadataUpdate<T>(GpuResource<T> resource, GpuMetadataChange change) where T : struct
    {
        if (resource.ResourceManager?.GetResourceRegistration(resource) is GpuResourceMetadataEntry<T> entry)
        {
            // Group admission already withdrew availability. Notify every image consumer now,
            // preserving any exception that caused the caller to abandon the command group.
            entry.NotifyImageDependents(suppressErrors: true);
            entry.NotifyFramebufferDependents(resource);
        }
        else DiscardUntrackedMetadata(resource, change);
    }

    /// <summary>Refreshes aliases after StateCache executes a native command, including rejected operations.</summary>
    internal static void ObserveWrapperMetadata<T>(GpuResource<T> resource, IGpuResourceMetadataReader<T> reader,
        GpuMetadataChange change) where T : struct
    {
        if (resource.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(resource))
        {
            var entry = manager.RegisterCreatedResource(resource.Kind, resource.ResourceId, reader);
            if (!ReferenceEquals(manager.GetResourceRegistration(resource), entry)) manager.AssociateResource(entry, resource);
            try
            {
                if (!manager.RefreshMetadata(entry, change))
                    throw new InvalidOperationException("Native resource metadata is unavailable after the command.");
            }
            finally
            {
                // The initiating framebuffer helper owns its pending image references and
                // completion event. Other wrappers must discard their previous configuration.
                entry.NotifyFramebufferDependents(resource);
            }
            return;
        }
        T data;
        try
        {
            if (!reader.TryRead(resource.ResourceId, out data))
                throw new InvalidOperationException("Native resource metadata is unavailable after the command.");
        }
        catch
        {
            DiscardUntrackedMetadata(resource, change);
            throw;
        }
        // Storage identity is independent of snapshot availability. Metadata-only recovery
        // cannot invalidate views, while even equal-sized storage updates retire their certificate.
        if ((change & GpuMetadataChange.Storage) != 0 && resource is GpuTexture texture)
            texture.UntrackedStorageIdentity = new();
        resource.HandleMetadataChanged(data);
    }

    /// <summary>Publishes command-proven facts supplied by StateCache, preserving native-width parameters.</summary>
    internal static void PublishObservedMetadata<T>(GpuResource<T> resource, T data, GpuMetadataChange change) where T : struct
    {
        if (resource.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(resource))
        {
            var entry = manager.RegisterCreatedResource<T>(resource.Kind, resource.ResourceId);
            if (!ReferenceEquals(manager.GetResourceRegistration(resource), entry)) manager.AssociateResource(entry, resource);
            manager.PublishMetadata(entry, data, change);
        }
        else resource.HandleMetadataChanged(data);
    }

    #endregion
}
