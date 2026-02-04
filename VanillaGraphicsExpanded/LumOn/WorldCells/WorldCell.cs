using VanillaGraphicsExpanded.Numerics;

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

        DesiredState = WorldCellDesiredState.Active;
        ActualState = WorldCellActualState.Active;
    }

    public WorldCellKey Key { get; }

    public WorldCellKind Kind => Key.Kind;

    public VectorInt3 CenterHalfBlockPos { get; protected set; }

    public float Priority { get; set; }

    public long NextEligibleTick { get; set; }

    public int CurrentVersion { get; set; }

    public int AppliedVersion { get; set; }

    public WorldCellDesiredState DesiredState { get; set; }

    public WorldCellActualState ActualState { get; set; }

    internal int HeapIndex { get; set; }

    internal int CooldownStreak { get; set; }

    internal long LastAttemptTick { get; set; }

    public abstract float CalculatePriority(in WorldCellPriorityContext context);

    public virtual WorldCellDesiredState CalculateDesiredState(in WorldCellStateTransitionContext context)
    {
        return WorldCellDesiredState.Active;
    }
}
