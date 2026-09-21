namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Render-thread residency backend, also usable by consumers with their own content work queues.</summary>
internal interface IPartitionResidencyBackend
{
    /// <summary>Acknowledges participation changes without a mandatory reupload.</summary>
    bool SetActive(in PartitionCellKey key, bool active);
    /// <summary>Makes dirty or departing contents inaccessible before reuse.</summary>
    void Invalidate(in PartitionCellKey key);
    /// <summary>Releases storage and participation on the render/owning thread, including uncaptured cells; delayed backends must defer reuse themselves.</summary>
    void Retire(in PartitionCellKey key);
}
