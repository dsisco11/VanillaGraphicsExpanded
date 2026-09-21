using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldCells;

public sealed class WorldCellKeyTests
{
    [Fact]
    public void FromTraceSceneRegion_UsesChunkKeyPacked()
    {
        ChunkKey chunkKey = ChunkKey.FromChunkCoords(1, 2, 3);

        WorldCellKey key = WorldCellKey.FromTraceSceneRegion(chunkKey.Packed);

        Assert.Equal(WorldCellKind.TraceSceneRegion, key.Kind);
        Assert.Equal(chunkKey.Packed, key.Packed);
    }

    [Fact]
    public void Equality_IncludesKind()
    {
        const ulong packed = 123ul;

        Assert.Equal(
            new WorldCellKey(WorldCellKind.TraceSceneRegion, packed),
            new WorldCellKey(WorldCellKind.TraceSceneRegion, packed));

        Assert.NotEqual(
            new WorldCellKey(WorldCellKind.TraceSceneRegion, packed),
            new WorldCellKey(WorldCellKind.LumonSceneNear, packed));
    }
}
