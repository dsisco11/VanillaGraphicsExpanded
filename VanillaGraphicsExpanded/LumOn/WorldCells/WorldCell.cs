namespace VanillaGraphicsExpanded.LumOn.WorldCells;

/// <summary>
/// Base implementation of <see cref="IWorldCell"/> with common scheduling bookkeeping.
/// </summary>
internal abstract class WorldCell : IWorldCell
{
    protected WorldCell(WorldCellKey key)
    {
        Key = key;
        HeapIndex = -1;
    }

    public WorldCellKey Key { get; }

    public WorldCellKind Kind => Key.Kind;

    public float Priority { get; set; }

    public long NextEligibleTick { get; set; }

    public int CurrentVersion { get; set; }

    public int AppliedVersion { get; set; }

    internal int HeapIndex { get; set; }

    internal int CooldownStreak { get; set; }

    internal long LastAttemptTick { get; set; }

    public abstract float CalculatePriority(in WorldCellPriorityContext context);
}
