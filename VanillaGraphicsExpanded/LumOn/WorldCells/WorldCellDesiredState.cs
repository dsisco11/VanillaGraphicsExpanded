namespace VanillaGraphicsExpanded.LumOn.WorldCells;

/// <summary>
/// High-level target lifecycle state for a <see cref="IWorldCell"/>.
/// </summary>
internal enum WorldCellDesiredState : byte
{
    /// <summary>
    /// Cell should not retain any residency/slot allocation.
    /// </summary>
    Unloaded = 0,

    /// <summary>
    /// Cell should retain residency (e.g. slot allocation) but should not be scheduled for update work.
    /// </summary>
    Loaded = 1,

    /// <summary>
    /// Cell is eligible to participate in scheduling for update work.
    /// </summary>
    Active = 2,
}
