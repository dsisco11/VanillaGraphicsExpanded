using System;
using System.Buffers;
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
                int baseX = chunkX << 5;
                int baseY = chunkY << 5;
                int baseZ = chunkZ << 5;

                // Rent + fill snapshot buffer (source-cell path).
                LumonSceneTraceSceneSourceCell[] buf = ArrayPool<LumonSceneTraceSceneSourceCell>.Shared.Rent(len);

                // If something fails while filling, make sure we return the array.
                try
                {
                    var blockAccessor = capi.World.BlockAccessor;
                    int maxY = capi.World.BlockAccessor.MapSizeY;
                    var pos = new BlockPos(0);

                    int i = 0;
                    for (int z = 0; z < size; z++)
                    {
                        int wz = baseZ + z;
                        for (int y = 0; y < size; y++)
                        {
                            int wy = baseY + y;
                            for (int x = 0; x < size; x++)
                            {
                                int wx = baseX + x;

                                if ((uint)wy >= (uint)maxY)
                                {
                                    buf[i++] = default;
                                    continue;
                                }

                                pos.Set(wx, wy, wz);
                                Block block = blockAccessor.GetBlock(pos);
                                if (block is null)
                                {
                                    buf[i++] = default;
                                    continue;
                                }

                                // v1 occupancy: treat blocks without collision boxes as empty (air/foliage/etc).
                                bool occupied = block.CollisionBoxes is not null && block.CollisionBoxes.Length > 0;
                                if (!occupied)
                                {
                                    buf[i++] = default;
                                    continue;
                                }

                                int blockLevel = blockAccessor.GetLightLevel(wx, wy, wz, EnumLightLevelType.OnlyBlockLight);
                                int sunLevel = blockAccessor.GetLightLevel(wx, wy, wz, EnumLightLevelType.OnlySunLight);

                                int rgb = blockAccessor.GetLightRGBsAsInt(wx, wy, wz);
                                int lightId = lightIds.GetOrAssignLightId(rgb);

                                // v1 material palette: stable placeholder derived from block id (does not encode per-face variation yet).
                                int materialPaletteIndex = block.Id & (int)LumonSceneOccupancyPacking.MaterialPaletteIndexMask;

                                buf[i++] = new LumonSceneTraceSceneSourceCell(
                                    isSolid: 1,
                                    blockLevel: (byte)Math.Clamp(blockLevel, 0, 32),
                                    sunLevel: (byte)Math.Clamp(sunLevel, 0, 32),
                                    lightId: (byte)Math.Clamp(lightId, 0, (int)LumonSceneOccupancyPacking.LightIdMask),
                                    materialPaletteIndex: (ushort)Math.Clamp(materialPaletteIndex, 0, (int)LumonSceneOccupancyPacking.MaterialPaletteIndexMask));
                            }
                        }
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
            catch
            {
                tcs.TrySetResult(null);
            }
        }, "vge-lumon-tracescene-snapshot");

        return new ValueTask<IChunkSnapshot?>(tcs.Task);
    }
}
