using System.Collections.Concurrent;
using System.Threading;

using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 23.5: Version provider for world-cell regions (32^3) keyed by <see cref="ChunkKey"/>.
/// Versions are driven by (chunk dirty) events plus explicit "rebuild all" generations.
/// </summary>
internal sealed class LumonSceneTraceSceneChunkVersionProvider : IChunkVersionProvider
{
    private readonly ConcurrentDictionary<ulong, int> localVersions = new();
    private int globalGeneration;

    public int GetCurrentVersion(ChunkKey key)
    {
        int local = localVersions.TryGetValue(key.Packed, out int v) ? v : 0;
        return unchecked(local + Volatile.Read(ref globalGeneration));
    }

    public void MarkDirty(ChunkKey key)
    {
        localVersions.AddOrUpdate(key.Packed, addValue: 1, updateValueFactory: static (_, prev) => unchecked(prev + 1));
    }

    public void BumpGlobalGeneration()
    {
        Interlocked.Increment(ref globalGeneration);
    }

    public void Reset()
    {
        localVersions.Clear();
        Volatile.Write(ref globalGeneration, 0);
    }
}

