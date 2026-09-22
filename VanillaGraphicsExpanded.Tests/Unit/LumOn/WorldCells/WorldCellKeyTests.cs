using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldCells;

/// <summary>Verifies consumer-local surface queue identities after geometry residency moved to partition keys.</summary>
public sealed class WorldCellKeyTests
{
    /// <summary>Preserves the source packed coordinate in a near-surface work token.</summary>
    [Fact]
    public void FromLumonSceneNear_UsesChunkKeyPacked()
    {
        ChunkKey chunkKey = ChunkKey.FromChunkCoords(1, 2, 3);

        WorldCellKey key = WorldCellKey.FromLumonSceneNear(chunkKey.Packed);

        Assert.Equal(WorldCellKind.LumonSceneNear, key.Kind);
        Assert.Equal(chunkKey.Packed, key.Packed);
    }

    /// <summary>Separates near and far consumer queues even when their packed coordinates match.</summary>
    [Fact]
    public void Equality_IncludesKind()
    {
        const ulong packed = 123ul;

        Assert.Equal(
            new WorldCellKey(WorldCellKind.LumonSceneNear, packed),
            new WorldCellKey(WorldCellKind.LumonSceneNear, packed));

        Assert.NotEqual(
            new WorldCellKey(WorldCellKind.LumonSceneNear, packed),
            new WorldCellKey(WorldCellKind.LumonSceneFar, packed));
    }
}
