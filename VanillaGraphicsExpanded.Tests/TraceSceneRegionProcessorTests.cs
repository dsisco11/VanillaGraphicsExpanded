using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public sealed class TraceSceneRegionProcessorTests
{
    [Fact]
    public async Task Packs_SourceCells_ToOccupancyWords()
    {
        var key = ChunkKey.FromChunkCoords(10, 2, -3);
        const int version = 123;

        int n = LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount;
        var buf = ArrayPool<LumonSceneTraceSceneSourceCell>.Shared.Rent(n);
        Array.Clear(buf, 0, n);
        var snap = new PooledChunkSnapshot<LumonSceneTraceSceneSourceCell>(
            key: key,
            version: version,
            sizeX: 32,
            sizeY: 32,
            sizeZ: 32,
            buffer: buf,
            length: n);

        try
        {
            // First cell: empty -> 0
            buf[0] = new LumonSceneTraceSceneSourceCell(
                isSolid: 0,
                blockLevel: 32,
                sunLevel: 32,
                lightId: 63,
                materialPaletteIndex: 1234);

            // Second cell: solid -> packed
            buf[1] = new LumonSceneTraceSceneSourceCell(
                isSolid: 1,
                blockLevel: 12,
                sunLevel: 3,
                lightId: 7,
                materialPaletteIndex: 999);

            var proc = new LumonSceneTraceSceneRegionProcessor();
            LumonSceneTraceSceneRegionArtifact art = await proc.ProcessAsync(snap, CancellationToken.None);

            Assert.Equal(key, art.Key);
            Assert.Equal(version, art.Version);
            Assert.Equal(n, art.PayloadWords.Length);

            Assert.Equal(0u, art.PayloadWords[0]);
            uint expected = LumonSceneOccupancyPacking.Pack(12, 3, 7, 999);
            Assert.Equal(expected, art.PayloadWords[1]);
        }
        finally
        {
            snap.Dispose();
        }
    }

    [Fact]
    public async Task Copies_PackedWords_Snapshots()
    {
        var key = ChunkKey.FromChunkCoords(0, 0, 0);
        const int version = 1;

        int n = LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount;
        var buf = ArrayPool<uint>.Shared.Rent(n);
        Array.Clear(buf, 0, n);
        var snap = new PooledChunkSnapshot<uint>(
            key: key,
            version: version,
            sizeX: 32,
            sizeY: 32,
            sizeZ: 32,
            buffer: buf,
            length: n);

        try
        {
            buf[0] = 0xDEADBEEFu;
            buf[1] = 123u;

            var proc = new LumonSceneTraceSceneRegionProcessor();
            LumonSceneTraceSceneRegionArtifact art = await proc.ProcessAsync(snap, CancellationToken.None);

            Assert.Equal(0xDEADBEEFu, art.PayloadWords[0]);
            Assert.Equal(123u, art.PayloadWords[1]);
        }
        finally
        {
            snap.Dispose();
        }
    }
}
