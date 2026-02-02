using System;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldCells;

/// <summary>
/// Per-frame or per-tick context used to compute a cell priority.
/// </summary>
internal readonly record struct WorldCellPriorityContext(
    VectorInt3 CameraBlockPos,
    VectorInt3 AnchorBlockPos,
    bool HasAnchor,
    VectorInt3 WindowMinRegion,
    VectorInt3 WindowMaxRegion,
    bool HasWindow,
    long NowTick,
    Func<WorldCellKey, bool>? IsCellLikelyLoaded = null);
