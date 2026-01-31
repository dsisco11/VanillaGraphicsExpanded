using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Chunk budgeting helper for LumonScene physical page planning.
/// </summary>
/// <remarks>
/// <para>
/// Covered chunks are the core 3D chunk window size (no extra margin): <c>(2*Rxy+1)×(2*Ry+1)×(2*Rxy+1)</c>.
/// </para>
/// <para>
/// Extra chunks are an additional physical-page budget (not extra chunk slots) used to absorb re-anchors.
/// </para>
/// </remarks>
internal readonly record struct LumonSceneFieldChunkBudget(
    int RadiusXZChunks,
    int RadiusYChunks,
    VectorInt3 DimsChunks,
    int CoveredChunks,
    int ExtraChunks,
    int TotalChunks);

