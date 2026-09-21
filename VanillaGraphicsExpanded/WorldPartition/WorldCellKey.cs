namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Consumer-local work queue token; shared residency identity is <see cref="PartitionCellKey"/>.</summary>
/// <remarks>Kind and packed coordinates are scoped to one consumer, never used as a global registry key.</remarks>
internal readonly record struct WorldCellKey(WorldCellKind Kind, ulong Packed)
{
    #region Domain tokens
    /// <summary>Creates a tracing work token from the adapter's packed coordinate.</summary>
    public static WorldCellKey FromTraceSceneRegion(ulong packedChunkKey) => new(WorldCellKind.TraceSceneRegion, packedChunkKey);
    /// <summary>Creates a near-scene content queue token.</summary>
    public static WorldCellKey FromLumonSceneNear(ulong packedChunkCoordKey) => new(WorldCellKind.LumonSceneNear, packedChunkCoordKey);
    /// <summary>Creates a far-scene content queue token.</summary>
    public static WorldCellKey FromLumonSceneFar(ulong packedChunkCoordKey) => new(WorldCellKind.LumonSceneFar, packedChunkCoordKey);
    #endregion
}