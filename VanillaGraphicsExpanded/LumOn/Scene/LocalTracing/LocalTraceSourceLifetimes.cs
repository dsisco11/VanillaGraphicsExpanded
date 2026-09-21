using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;

/// <summary>Invalidates source versions when a loaded chunk instance disappears or is replaced.</summary>
internal sealed class LocalTraceSourceLifetimes
{
    private readonly Dictionary<ChunkKey, object> observed = new();
    private readonly LumonSceneTraceSceneChunkVersionProvider versions;

    /// <summary>Uses the same dependency revisions as the chunk processing artifact cache.</summary>
    public LocalTraceSourceLifetimes(LumonSceneTraceSceneChunkVersionProvider versions) => this.versions = versions;

    /// <summary>Records loaded identity before testing a snapshot's dependency version.</summary>
    public bool Observe(ChunkKey key, object? identity)
    {
        if (identity == null)
        {
            if (observed.Remove(key)) versions.MarkDirty(key);
            return false;
        }
        if (!observed.TryGetValue(key, out object? previous) || !ReferenceEquals(previous, identity))
        {
            observed[key] = identity;
            versions.MarkDirty(key);
        }
        return true;
    }

    /// <summary>Bounds identity memory to the current ring; revisits begin a new source lifetime.</summary>
    public void Retain(in PartitionCellRange cells)
    {
        foreach (ChunkKey key in observed.Keys.ToArray())
        {
            key.Decode(out int x, out int y, out int z);
            if (cells.Empty || x < (cells.Min.X >> 1) || y < (cells.Min.Y >> 1) || z < (cells.Min.Z >> 1) ||
                x > ((cells.End.X - 1) >> 1) || y > ((cells.End.Y - 1) >> 1) || z > ((cells.End.Z - 1) >> 1)) observed.Remove(key);
        }
    }
}
