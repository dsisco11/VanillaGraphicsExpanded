using System.Buffers;
using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Ensures asynchronous artifact production retains companion payload identity and lifetime.</summary>
public sealed class LocalTraceRegionArtifactTests
{
    #region Artifact Ownership
    /// <summary>Local source data survives snapshot disposal and retains its world key and requested generation.</summary>
    [Fact]
    public async Task Processor_CopiesLocalDataWithIdentity()
    {
        const int count = 32768;
        var source = ArrayPool<LumonSceneTraceSceneSourceCell>.Shared.Rent(count);
        Array.Clear(source, 0, count);
        var key = ChunkKey.FromChunkCoords(-3, 2, 7);
        using var snapshot = new PooledChunkSnapshot<LumonSceneTraceSceneSourceCell>(key, 23, 32, 32, 32, source, count);
        var expected = new LocalTraceSourceCell(6, new Vector4(0.1f, 0.2f, 0.3f, 0.4f));
        source[1027] = source[1027] with { LocalTrace = expected };
        var artifact = await new LumonSceneTraceSceneRegionProcessor().ProcessAsync(snapshot, CancellationToken.None);
        source[1027] = default;
        Assert.Equal(key, artifact.Key);
        Assert.Equal(23, artifact.Version);
        Assert.NotNull(artifact.LocalCells);
        Assert.Equal(expected, artifact.LocalCells[1027]);
        Assert.Equal((long)count * 24, artifact.EstimatedBytes);
    }

    /// <summary>Occupancy-only snapshots do not retain a full local-lighting array.</summary>
    [Fact]
    public async Task Processor_OmitsUncapturedLocalData()
    {
        const int count = 32768;
        var source = ArrayPool<LumonSceneTraceSceneSourceCell>.Shared.Rent(count);
        Array.Clear(source, 0, count);
        using var snapshot = new PooledChunkSnapshot<LumonSceneTraceSceneSourceCell>(default, 1, 32, 32, 32, source, count);
        var artifact = await new LumonSceneTraceSceneRegionProcessor().ProcessAsync(snapshot, CancellationToken.None);
        Assert.Null(artifact.LocalCells);
        Assert.Equal((long)count * 4, artifact.EstimatedBytes);
    }
    #endregion
}
