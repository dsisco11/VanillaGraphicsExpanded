namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>
/// Observed current lifecycle state for a <see cref="IWorldCell"/>.
/// </summary>
internal enum WorldCellActualState : byte
{
    Unloaded = 0,
    Loading = 1,
    Loaded = 2,
    Activating = 3,
    Active = 4,
    Unloading = 5,
}
