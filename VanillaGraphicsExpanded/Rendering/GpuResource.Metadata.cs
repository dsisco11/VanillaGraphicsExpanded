namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Stores the synchronized native metadata snapshot of a resource wrapper.</summary>
public abstract class GpuResource<TMetadata> : GpuResource where TMetadata : struct
{
    private TMetadata metadataSnapshot;
    private static readonly TMetadata unavailableMetadata = default;
    /// <summary>Retains the distinction between no supplied facts and a committed empty local schema.</summary>
    internal bool HasAssignedMetadata { get; private set; }
    /// <summary>Distinguishes a supplied local snapshot from an explicitly withdrawn one.</summary>
    internal bool HasMetadataSnapshot { get; private set; }
    /// <summary>Retains uncertain local operation effects until explicit recovery supplies complete facts.</summary>
    internal GpuMetadataChange PendingMetadataEffects { get; private set; }

    /// <summary>Supplies the published snapshot to manager-owned consumers without acquisition.</summary>
    internal TMetadata PublishedMetadata => metadataSnapshot;

    #region Internal API
    /// <summary>Replaces this wrapper's snapshot without querying native state or altering ownership.</summary>
    internal void AssignMetadataSnapshot(TMetadata data)
    {
        metadataSnapshot = data;
        HasAssignedMetadata = HasMetadataSnapshot = true;
        PendingMetadataEffects = GpuMetadataChange.None;
    }
    /// <summary>Closes local snapshot usability without fabricating an available empty schema.</summary>
    internal void WithdrawMetadataSnapshot(GpuMetadataChange effects = GpuMetadataChange.None)
    {
        HasMetadataSnapshot = false;
        PendingMetadataEffects |= effects;
    }
    #endregion

    #region Protected API
    /// <summary>Exposes the manager-published snapshot without allowing derived wrappers to edit it.</summary>
    protected ref readonly TMetadata metadata => ref (HasMetadataSnapshot ? ref metadataSnapshot : ref unavailableMetadata);
    #endregion
}
