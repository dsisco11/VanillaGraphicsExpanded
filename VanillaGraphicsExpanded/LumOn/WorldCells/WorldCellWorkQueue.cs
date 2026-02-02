namespace VanillaGraphicsExpanded.LumOn.WorldCells;

/// <summary>
/// Identifies a coarse work queue a cell can enqueue itself into.
/// </summary>
internal enum WorldCellWorkQueue : byte
{
    /// <summary>
    /// Priority-ordered eligibility queue for near-window work.
    /// </summary>
    EligibleNear = 0,

    /// <summary>
    /// Priority-ordered eligibility queue for far-window work.
    /// </summary>
    EligibleFar = 1,

    /// <summary>
    /// Queue for state transitions (allocate slot, deactivate, unload, etc.).
    /// </summary>
    StateTransition = 2,

    /// <summary>
    /// Queue for capture work (page population).
    /// </summary>
    Capture = 3,

    /// <summary>
    /// Queue for relight work.
    /// </summary>
    Relight = 4,
}
