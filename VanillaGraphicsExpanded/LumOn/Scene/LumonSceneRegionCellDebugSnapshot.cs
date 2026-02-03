using VanillaGraphicsExpanded.LumOn.WorldCells;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal readonly record struct LumonSceneRegionCellDebugSnapshot(
    VectorInt3 ChunkCoord,
    WorldCellDesiredState DesiredState,
    WorldCellActualState ActualState);
