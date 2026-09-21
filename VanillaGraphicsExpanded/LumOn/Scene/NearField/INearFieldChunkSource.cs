using System;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Nonblocking source adapter; missing chunks are never synthesized as empty geometry.</summary>
internal interface INearFieldChunkSource
{
    /// <summary>Returns a current immutable chunk or schedules bounded work and reports unavailable.</summary>
    bool TryGet(ChunkKey key, out NearFieldChunkSnapshot? snapshot);
    /// <summary>Checks dependency version and availability on the owning thread.</summary>
    bool IsCurrent(ChunkKey key, int version);
}
