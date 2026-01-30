using System;
using System.Buffers;
using System.Collections.Generic;
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
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetResult(null);
                return;
            }

            // If the chunk version changed before we got to the main thread, treat this as unavailable
            // (the processing service will likely supersede the request anyway).
            if (versionProvider.GetCurrentVersion(key) != expectedVersion)
            {
                tcs.TrySetResult(null);
                return;
            }

            try
            {
                const int size = LumonSceneTraceSceneClipmapMath.RegionSize; // 32
                const int len = LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount; // 32^3

                key.Decode(out int chunkX, out int chunkY, out int chunkZ);

                // Grab the backing world-chunk directly and unpack before reading blocks/lighting.
                // This avoids per-block BlockAccessor calls.
                IBlockAccessor blockAccessor = capi.World.BlockAccessor;
                IWorldChunk? chunk = blockAccessor.GetChunk(chunkX, chunkY, chunkZ);
                if (chunk is null || chunk.Disposed)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                if (!chunk.Unpack_ReadOnly())
                {
                    tcs.TrySetResult(null);
                    return;
                }

                IChunkBlocks blocks = chunk.Data;
                IChunkLight lighting = chunk.Lighting;

                // Clone chunk block ids under a bulk read lock, then release quickly.
                int[] blockIds = ArrayPool<int>.Shared.Rent(len);
                try
                {
                    blocks.TakeBulkReadLock();
                    try
                    {
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

                        // Precompute world-space base for optional color-light lookup fallback.
                        int baseX = chunkX << 5;
                        int baseY = chunkY << 5;
                        int baseZ = chunkZ << 5;

                        for (int i = 0; i < len; i++)
                        {
                            ct.ThrowIfCancellationRequested();

                            int blockId = blockIds[i];
                            if (blockId == 0)
                            {
                                buf[i] = default;
                                continue;
                            }

                            if (!solidByBlockId.TryGetValue(blockId, out bool solid))
                            {
                                Block block = capi.World.GetBlock(blockId);
                                solid = block?.CollisionBoxes is not null && block.CollisionBoxes.Length > 0;
                                solidByBlockId[blockId] = solid;
                            }

                            // v1 occupancy: treat blocks without collision boxes as empty (air/foliage/etc).
                            if (!solid)
                            {
                                buf[i] = default;
                                continue;
                            }

                            int blockLevel = lighting.GetBlocklight(i);
                            int sunLevel = lighting.GetSunlight(i);

                            // v1 colored light: keep the old RGB accessor only when blocklight is non-zero.
                            // (Chunk lighting exposes levels efficiently; RGB composition is still via the engine path.)
                            int lightId = 0;
                            if (blockLevel > 0)
                            {
                                int x = i & 31;
                                int y = (i >> 5) & 31;
                                int z = i >> 10;

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
                tcs.TrySetResult(null);
            }
        }, "vge-lumon-tracescene-snapshot");

        return new ValueTask<IChunkSnapshot?>(tcs.Task);
    }
}
