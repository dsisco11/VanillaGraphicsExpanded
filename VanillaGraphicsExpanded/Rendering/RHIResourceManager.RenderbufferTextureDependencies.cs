using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Connects renderbuffer texture associations to canonical renderbuffer storage availability.</summary>
internal sealed partial class RHIResourceManager
{
    #region Private
    /// <summary>Acquires missing backing facts for an established association, reusing available original entries.</summary>
    private void AcquireRenderbufferTextureBacking<TMetadata>(GpuResourceRegistration entry, TMetadata candidate,
        bool renewAssociation) where TMetadata : struct
    {
        if (candidate is not GpuTextureMetadata { Renderbuffer: not 0 } texture) return;
        RequireOriginalTextureBacking(entry, GpuResourceKind.Renderbuffer, texture.Renderbuffer, renewAssociation);
        var backing = LookupAvailableResource<GpuRenderbufferMetadata>(GpuResourceKind.Renderbuffer, texture.Renderbuffer);
        if (backing is null)
            throw new InvalidOperationException("Renderbuffer texture backing metadata is unavailable.");
        RecordTextureBacking(entry, backing);
    }

    /// <summary>Rejects dependent use while the backing object's canonical storage is unavailable.</summary>
    private bool IsRenderbufferTextureBackingAvailable(GpuResourceRegistration entry)
    {
        return entry is not GpuResourceMetadataEntry<GpuTextureMetadata> texture
            || texture.Metadata.Renderbuffer == 0
            || IsOriginalTextureBackingAvailable(entry);
    }

    /// <summary>Revises surviving texture dependencies after backing storage changes without inventing mip images.</summary>
    private void PublishRenderbufferTextureDependencies(GpuResourceRegistration backing, ulong previousStorage)
    {
        if (backing.Kind != GpuResourceKind.Renderbuffer || backing.StorageRevision == previousStorage) return;
        List<Exception>? failures = null;
        foreach (var pair in metadataRegistry)
        {
            if (pair.Value is not GpuResourceMetadataEntry<GpuTextureMetadata> texture
                || !texture.IsUsable || texture.Metadata.Renderbuffer != backing.Id) continue;
            // Renderbuffer textures have no levels of their own. Refresh the dependency's
            // revision while retaining its queried association and separate backing facts.
            try { PublishMetadata(texture, texture.Metadata, GpuMetadataChange.Storage); }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }
        if (failures is not null)
            throw new AggregateException("Renderbuffer texture dependency publication failed.", failures);
    }
    #endregion
}
