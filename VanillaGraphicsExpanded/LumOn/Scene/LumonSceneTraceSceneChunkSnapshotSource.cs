using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 23.5: Snapshot source that extracts a 32^3 world-cell region payload from the client world.
/// </summary>
/// <remarks>
/// VintageStory world access is not assumed to be thread-safe, so this source marshals snapshot extraction
/// onto the main thread via <see cref="ICoreClientAPI.Event"/>.
/// </remarks>
internal sealed class LumonSceneTraceSceneChunkSnapshotSource : IChunkSnapshotSource
{
    private readonly ICoreClientAPI capi;
    private readonly LumonSceneTraceSceneChunkVersionProvider versionProvider;
    private readonly LumonSceneTraceSceneLightIdRegistry lightIds;

    public LumonSceneTraceSceneChunkSnapshotSource(
        ICoreClientAPI capi,
        LumonSceneTraceSceneChunkVersionProvider versionProvider,
        LumonSceneTraceSceneLightIdRegistry lightIds)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
        this.lightIds = lightIds ?? throw new ArgumentNullException(nameof(lightIds));
    }

    public ValueTask<IChunkSnapshot?> TryCreateSnapshotAsync(ChunkKey key, int expectedVersion, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return ValueTask.FromResult<IChunkSnapshot?>(null);
        }

        var tcs = new TaskCompletionSource<IChunkSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);

        capi.Event.EnqueueMainThreadTask(() =>
        {
            LumonSceneTraceSceneMetrics.OnSnapshotRequested();

            if (ct.IsCancellationRequested)
            {
                LumonSceneTraceSceneMetrics.OnSnapshotFailedCanceled();
                LumonSceneTraceSceneMetrics.OnSnapshotUnavailable();
                tcs.TrySetResult(null);
                return;
            }

            try
            {
                const int size = LumonSceneTraceSceneClipmapMath.RegionSize; // 32
                const int len = LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount; // 32^3

                key.Decode(out int chunkX, out int chunkY, out int chunkZ);
                int currentVersion = versionProvider.GetCurrentVersion(key);

                // Grab the backing world-chunk directly and unpack before reading blocks/lighting.
                // Prefer GetChunkAtBlockPos (used elsewhere in VGE) to avoid any coordinate-order ambiguity.
                IBlockAccessor blockAccessor = capi.World.BlockAccessor;
                int baseX = chunkX << 5;
                int baseY = chunkY << 5;
                int baseZ = chunkZ << 5;

                var chunkPos = new BlockPos(0);
                chunkPos.Set(baseX, baseY, baseZ);
                IWorldChunk? chunk = blockAccessor.GetChunkAtBlockPos(chunkPos)
                                   ?? blockAccessor.GetChunk(chunkX, chunkY, chunkZ);
                if (chunk is null || chunk.Disposed)
                {
                    LumonSceneTraceSceneMetrics.OnSnapshotFailedChunkMissing();
                    LumonSceneTraceSceneMetrics.OnSnapshotUnavailable();
                    LumonSceneTraceSceneMetrics.SetLastSnapshotInfo(
                        chunkX: chunkX,
                        chunkY: chunkY,
                        chunkZ: chunkZ,
                        expectedVersion: expectedVersion,
                        currentVersion: currentVersion,
                        chunkFound: false,
                        bulkCloneUsed: false,
                        blockId0: 0,
                        blockIdCenter: 0,
                        blockIdLast: 0,
                        directBlockIdCenter: 0,
                        nonAirCells: 0,
                        solidCells: 0);
                    tcs.TrySetResult(null);
                    return;
                }

                // Must be called before accessing block/light data, but the return value does not indicate availability.
                // (In practice it is used to indicate whether an unpack actually occurred.)
                chunk.Unpack_ReadOnly();

                if (chunk.Disposed)
                {
                    LumonSceneTraceSceneMetrics.OnSnapshotFailedChunkMissing();
                    LumonSceneTraceSceneMetrics.OnSnapshotUnavailable();
                    LumonSceneTraceSceneMetrics.SetLastSnapshotInfo(
                        chunkX: chunkX,
                        chunkY: chunkY,
                        chunkZ: chunkZ,
                        expectedVersion: expectedVersion,
                        currentVersion: currentVersion,
                        chunkFound: false,
                        bulkCloneUsed: false,
                        blockId0: 0,
                        blockIdCenter: 0,
                        blockIdLast: 0,
                        directBlockIdCenter: 0,
                        nonAirCells: 0,
                        solidCells: 0);
                    tcs.TrySetResult(null);
                    return;
                }

                IChunkBlocks blocks = chunk.Data;
                IChunkLight lighting = chunk.Lighting;

                // Clone chunk block ids under a bulk read lock, then release quickly.
                int[] blockIds = ArrayPool<int>.Shared.Rent(len);
                try
                {
                    bool bulkCloneUsed = false;

                    blocks.TakeBulkReadLock();
                    try
                    {
                        // NOTE: do not reflect/copy IChunkBlocks.Data directly. The backing storage can be packed
                        // (palette/compressed) and is not guaranteed to be raw block IDs.
                        // The only correct fast path is the engine accessor under the bulk read lock.
                        for (int i = 0; i < len; i++)
                        {
                            blockIds[i] = blocks.GetBlockIdUnsafe(i);
                        }
                    }
                    finally
                    {
                        blocks.ReleaseBulkReadLock();
                    }

                    // Rent + fill snapshot buffer (source-cell path).
                    LumonSceneTraceSceneSourceCell[] buf = ArrayPool<LumonSceneTraceSceneSourceCell>.Shared.Rent(len);

                    try
                    {
                        // Cache collision/occupancy decisions per block id (huge win vs per-voxel block lookup).
                        var solidByBlockId = new Dictionary<int, bool>(capacity: 128);

                        var pos = new BlockPos(0);

                        int nonAirCells = 0;
                        int solidCells = 0;

                        int blockId0 = blockIds[0];
                        int blockIdCenter = blockIds[(16 << 10) | (16 << 5) | 16];
                        int blockIdLast = blockIds[len - 1];

                        // Cross-check against the accessor path (if chunk data returns zeros but accessor doesn't,
                        // we know we're reading the wrong backing chunk buffer).
                        int directBlockIdCenter = 0;
                        try
                        {
                            pos.Set(baseX + 16, baseY + 16, baseZ + 16);
                            directBlockIdCenter = blockAccessor.GetBlockId(pos);
                        }
                        catch
                        {
                            directBlockIdCenter = 0;
                        }

                        for (int i = 0; i < len; i++)
                        {
                            ct.ThrowIfCancellationRequested();

                            int blockId = blockIds[i];
                            if (blockId == 0)
                            {
                                buf[i] = default;
                                continue;
                            }

                            nonAirCells++;

                            int x = i & 31;
                            int y = (i >> 5) & 31;
                            int z = i >> 10;

                            if (!solidByBlockId.TryGetValue(blockId, out bool solid))
                            {
                                Block? block = capi.World.GetBlock(blockId);

                                // Use the engine's collision query rather than the raw CollisionBoxes property.
                                // Many blocks compute collision boxes dynamically (shape/rotation/etc) and the property
                                // may be empty even when collisions exist.
                                pos.Set(baseX + x, baseY + y, baseZ + z);
                                Cuboidf[]? boxes = block?.GetCollisionBoxes(blockAccessor, pos);
                                solid = boxes is not null && boxes.Length > 0;
                                solidByBlockId[blockId] = solid;
                            }

                            // v1 occupancy: treat blocks without collision boxes as empty (air/foliage/etc).
                            if (!solid)
                            {
                                buf[i] = default;
                                continue;
                            }

                            solidCells++;

                            int blockLevel = lighting.GetBlocklight(i);
                            int sunLevel = lighting.GetSunlight(i);

                            // v1 colored light: keep the old RGB accessor only when blocklight is non-zero.
                            // (Chunk lighting exposes levels efficiently; RGB composition is still via the engine path.)
                            int lightId = 0;
                            if (blockLevel > 0)
                            {
                                int rgb = blockAccessor.GetLightRGBsAsInt(baseX + x, baseY + y, baseZ + z) & 0x00FFFFFF;
                                lightId = lightIds.GetOrAssignLightId(rgb);
                            }

                            // v1 material palette: stable placeholder derived from block id (does not encode per-face variation yet).
                            int materialPaletteIndex = blockId & (int)LumonSceneOccupancyPacking.MaterialPaletteIndexMask;

                            buf[i] = new LumonSceneTraceSceneSourceCell(
                                isSolid: 1,
                                blockLevel: (byte)Math.Clamp(blockLevel, 0, 32),
                                sunLevel: (byte)Math.Clamp(sunLevel, 0, 32),
                                lightId: (byte)Math.Clamp(lightId, 0, (int)LumonSceneOccupancyPacking.LightIdMask),
                                materialPaletteIndex: (ushort)Math.Clamp(materialPaletteIndex, 0, (int)LumonSceneOccupancyPacking.MaterialPaletteIndexMask));
                        }

                        var snapshot = new PooledChunkSnapshot<LumonSceneTraceSceneSourceCell>(
                            key: key,
                            version: expectedVersion,
                            sizeX: size,
                            sizeY: size,
                            sizeZ: size,
                            buffer: buf,
                            length: len);

                        LumonSceneTraceSceneMetrics.OnSnapshotSucceeded();
                        LumonSceneTraceSceneMetrics.SetLastSnapshotInfo(
                            chunkX: chunkX,
                            chunkY: chunkY,
                            chunkZ: chunkZ,
                            expectedVersion: expectedVersion,
                            currentVersion: currentVersion,
                            chunkFound: true,
                            bulkCloneUsed: bulkCloneUsed,
                            blockId0: blockId0,
                            blockIdCenter: blockIdCenter,
                            blockIdLast: blockIdLast,
                            directBlockIdCenter: directBlockIdCenter,
                            nonAirCells: nonAirCells,
                            solidCells: solidCells);

                        tcs.TrySetResult(snapshot);
                        buf = null!;
                    }
                    finally
                    {
                        if (buf is not null)
                        {
                            ArrayPool<LumonSceneTraceSceneSourceCell>.Shared.Return(buf);
                        }
                    }
                }
                finally
                {
                    ArrayPool<int>.Shared.Return(blockIds, clearArray: false);
                }
            }
            catch
            {
                LumonSceneTraceSceneMetrics.OnSnapshotFailedException();
                LumonSceneTraceSceneMetrics.OnSnapshotUnavailable();
                tcs.TrySetResult(null);
            }
        }, "vge-lumon-tracescene-snapshot");

        return new ValueTask<IChunkSnapshot?>(tcs.Task);
    }
}
