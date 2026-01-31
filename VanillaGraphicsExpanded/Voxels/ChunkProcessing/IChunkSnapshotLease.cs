namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

/// <summary>
/// Internal contract for snapshot wrappers that allow processors to unwrap to the underlying concrete snapshot type.
/// </summary>
/// <remarks>
/// The chunk processing service may wrap snapshots to implement shared-lifetime semantics.
/// Some processors pattern-match on concrete snapshot types (e.g. <c>PooledChunkSnapshot&lt;T&gt;</c>),
/// so the underlying snapshot must remain accessible.
/// </remarks>
internal interface IChunkSnapshotLease : IChunkSnapshot
{
    IChunkSnapshot InnerSnapshot { get; }
}

