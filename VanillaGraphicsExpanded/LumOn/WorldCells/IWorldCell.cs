using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldCells;

/// <summary>
/// Contract for a schedulable unit of work representing a sub-partition of world space.
/// </summary>
internal interface IWorldCell
{
    WorldCellKey Key { get; }

    WorldCellKind Kind { get; }

    /// <summary>
    /// Center point of the cell in world space, expressed in half-block units.
    /// (e.g. block center at x=0.5 is <c>x=1</c>).
    /// Used for distance-based prioritization and desired-state evaluation.
    /// </summary>
    VectorInt3 CenterHalfBlockPos { get; }

    float Priority { get; set; }

    long NextEligibleTick { get; set; }

    int CurrentVersion { get; set; }

    int AppliedVersion { get; set; }

    WorldCellDesiredState DesiredState { get; set; }

    WorldCellActualState ActualState { get; set; }

    float CalculatePriority(in WorldCellPriorityContext context);

    WorldCellDesiredState CalculateDesiredState(in WorldCellStateTransitionContext context);
}
