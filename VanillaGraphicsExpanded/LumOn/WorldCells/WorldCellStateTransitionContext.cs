using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldCells;

/// <summary>
/// Context used to compute desired/actual state transitions for a cell.
/// </summary>
internal readonly record struct WorldCellStateTransitionContext(
    VectorInt3 CameraBlockPos,
    VectorInt3 AnchorBlockPos,
    bool HasAnchor,
    VectorInt3 LoadedWindowMinRegion,
    VectorInt3 LoadedWindowMaxRegion,
    bool HasLoadedWindow,
    VectorInt3 ActiveWindowMinRegion,
    VectorInt3 ActiveWindowMaxRegion,
    bool HasActiveWindow,
    long NowTick,
    System.Func<WorldCellKey, bool>? IsCellLikelyLoaded = null);
