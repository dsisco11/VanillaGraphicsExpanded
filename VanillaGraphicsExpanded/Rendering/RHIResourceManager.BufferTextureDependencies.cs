using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Propagates observed backing-buffer availability and storage revisions to texture associations.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Retains a validated EXT buffer association and its original backing until target discovery.</summary>
    internal void RetainInitialBufferTextureRequest(GpuResourceMetadataEntry<GpuTextureMetadata> texture,
        GpuTextureBufferMetadata association, GpuResourceRegistration? backing)
    {
        RequireMetadataContext();
        RequireLiveEntry(texture);
        // Buffer textures have no image mip/sampler state in the canonical schema.
        // The validated association supplies every remaining field once native target
        // discovery confirms that this name actually denotes a buffer texture.
        texture.RetainInitialCandidate(new(RHI.TextureDimension.Buffer,
            System.Collections.Immutable.ImmutableArray<GpuTextureLevelMetadata>.Empty, false, 0, 0, 0, association));
        if (backing is not null) RecordTextureBacking(texture, backing);
        else
        {
            lock (dependencySync)
            {
                unresolvedDependencies.Remove(texture);
                unresolvedDependencies.Add(texture, unresolvedDependency);
            }
        }
    }
    #endregion

    #region Private
    /// <summary>Acquires the actual backing dependency of a published texture without requiring a buffer wrapper.</summary>
    private void AcquireBufferTextureBacking<TMetadata>(GpuResourceRegistration texture, TMetadata candidate,
        bool renewAssociation) where TMetadata : struct
    {
        if (candidate is not GpuTextureMetadata { Buffer: { BufferId: not 0 } association })
        {
            if (candidate is GpuTextureMetadata && textureBackingIdentities.TryGetValue(texture, out var old)
                && old.Source.Kind == GpuResourceKind.Buffer && !old.StorageRevision.HasValue) RemoveTextureBacking(texture);
            return;
        }
        // A concrete texture association makes this an observed dependency, not a speculative
        // registration of arbitrary native objects. Existing usable entries need no readback.
        RequireOriginalTextureBacking(texture, GpuResourceKind.Buffer, association.BufferId, renewAssociation);
        var backing = LookupAvailableResource<GpuBufferMetadata>(GpuResourceKind.Buffer, association.BufferId);
        if (backing is null)
            throw new InvalidOperationException("Buffer texture backing metadata is unavailable.");
        RecordTextureBacking(texture, backing);
    }

    /// <summary>Requires available backing facts when the associated buffer participates in this registry.</summary>
    private bool IsBufferTextureBackingAvailable(GpuResourceRegistration entry)
    {
        if (entry is not GpuResourceMetadataEntry<GpuTextureMetadata> texture
            || texture.Metadata.Buffer is not { BufferId: not 0 } association) return true;
        // Test the captured registration, not whichever allocation currently uses its name.
        return IsOriginalTextureBackingAvailable(entry);
    }

    /// <summary>Renews dependent storage revisions without changing the native texture association or range mode.</summary>
    private void PublishBufferTextureDependencies(GpuResourceRegistration backing, ulong previousStorage)
    {
        if (backing.Kind != GpuResourceKind.Buffer || backing.StorageRevision == previousStorage) return;
        System.Collections.Generic.List<Exception>? failures = null;
        foreach (var dependent in GetResourceDependents(backing))
        {
            if (dependent is not GpuResourceMetadataEntry<GpuTextureMetadata> texture
                || !texture.IsUsable || texture.Metadata.Buffer is not { } association
                || association.BufferId != backing.Id
                || !textureBackingIdentities.TryGetValue(texture, out var original) || !ReferenceEquals(original.Source, backing)) continue;
            // Buffer storage commands cannot alter a texture's association, explicit range,
            // or whole-buffer mode. Those proven descriptors remain valid; their backing
            // allocation has changed. Never recover a texture withdrawn for another reason.
            try { PublishMetadata(texture, texture.Metadata, GpuMetadataChange.Storage); }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }
        if (failures is not null) throw new AggregateException("Buffer texture dependency publication failed.", failures);
    }
    #endregion
}
