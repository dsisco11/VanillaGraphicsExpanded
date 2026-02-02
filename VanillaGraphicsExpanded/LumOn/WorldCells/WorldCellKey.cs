using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.WorldCells;

/// <summary>
/// Compact, hash-friendly identifier for a "sub-partition" of world space.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Packed"/> is kind-specific payload. For <see cref="WorldCellKind.TraceSceneRegion"/>,
/// this is intentionally compatible with <see cref="ChunkKey.Packed"/>.
/// </para>
/// <para>
/// The combination of <see cref="Kind"/> and <see cref="Packed"/> is the full identity.
/// </para>
/// </remarks>
internal readonly record struct WorldCellKey(WorldCellKind Kind, ulong Packed)
{
    public static WorldCellKey FromTraceSceneRegion(ChunkKey chunkKey)
        => new(WorldCellKind.TraceSceneRegion, chunkKey.Packed);

    public static WorldCellKey FromTraceSceneRegion(ulong packedChunkKey)
        => new(WorldCellKind.TraceSceneRegion, packedChunkKey);
}
