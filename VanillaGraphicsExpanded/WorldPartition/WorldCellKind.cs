namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>
/// Identifies the high-level scheduling domain for a <see cref="WorldCellKey"/>.
/// </summary>
internal enum WorldCellKind : byte
{
    TraceSceneRegion = 0,
    LumonSceneNear = 1,
    LumonSceneFar = 2,
}
