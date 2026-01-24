namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

// Matches base-game face index order: N=0, E=1, S=2, W=3, U=4, D=5
// (see Vintagestory.API.MathTools.BlockFacing.index* constants).
internal enum ProbeHitFace : byte
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
    Up = 4,
    Down = 5,
}
