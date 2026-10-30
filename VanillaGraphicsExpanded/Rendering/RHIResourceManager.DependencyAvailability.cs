namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Reconciles existing original backing associations before consumer delivery.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Prevents source recovery from certifying facts made uncertain by an independent child operation.</summary>
    internal void ClearDependencyWithdrawal(GpuResourceRegistration resource)
    {
        if (textureBackingIdentities.TryGetValue(resource, out var backing)) backing.WithdrawnBySource = false;
    }

    /// <summary>Withdraws affected dependent facts while preserving unrelated uncertainty and original identities.</summary>
    internal void WithdrawResourceDependencies(GpuResourceRegistration source, GpuMetadataChange effects)
    {
        foreach (var pair in metadataRegistry)
        {
            if (pair.Value is not GpuResourceMetadataEntry<GpuTextureMetadata> texture
                || !textureBackingIdentities.TryGetValue(texture, out var backing) || !ReferenceEquals(backing.Source, source)) continue;
            if (texture.IsUsable) backing.WithdrawnBySource = true;
            texture.Withdraw(backing.StorageRevision.HasValue ? GpuMetadataChange.Metadata : effects, fromBacking: true);
        }
    }

    /// <summary>Restores only withdrawals caused by this source, never unrelated failed operations.</summary>
    internal void RecoverResourceDependencies(GpuResourceRegistration source)
    {
        foreach (var pair in metadataRegistry)
        {
            if (pair.Value is not GpuResourceMetadataEntry<GpuTextureMetadata> texture
                || !textureBackingIdentities.TryGetValue(texture, out var backing) || !ReferenceEquals(backing.Source, source)
                || !backing.WithdrawnBySource || !texture.HasCommittedMetadata || texture.OperationInProgress || texture.CompletionInProgress
                || backing.StorageRevision.HasValue && backing.StorageRevision.Value != source.StorageRevision) continue;
            backing.WithdrawnBySource = false;
            PublishMetadata(texture, texture.Metadata, GpuMetadataChange.None);
        }
    }
    #endregion
}
