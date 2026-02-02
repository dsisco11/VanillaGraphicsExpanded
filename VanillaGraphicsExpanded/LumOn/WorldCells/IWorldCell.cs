namespace VanillaGraphicsExpanded.LumOn.WorldCells;

/// <summary>
/// Contract for a schedulable unit of work representing a sub-partition of world space.
/// </summary>
internal interface IWorldCell
{
    WorldCellKey Key { get; }

    WorldCellKind Kind { get; }

    float Priority { get; set; }

    long NextEligibleTick { get; set; }

    int CurrentVersion { get; set; }

    int AppliedVersion { get; set; }

    float CalculatePriority(in WorldCellPriorityContext context);
}
