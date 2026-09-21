using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.LumOn.Scene.NearField;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Captures one shared geometry/light source on the existing chunk workers using bulk layer reads.</summary>
internal sealed class TraceGeometrySnapshotSource : IChunkSnapshotSource
{
    private readonly IBlockAccessor accessor;
    private readonly System.Func<int, Block> blocks;
    private readonly IChunkVersionProvider versions;
    private readonly TraceGeometryMaterials materials;
    private readonly NearFieldLightDecoder decoder;

    /// <summary>Injects engine access and scene-generation tables; no main-thread task dispatch occurs here.</summary>
    public TraceGeometrySnapshotSource(IBlockAccessor accessor, System.Func<int, Block> blocks, IChunkVersionProvider versions,
        TraceGeometryMaterials materials, NearFieldLightDecoder decoder)
    { this.accessor = accessor; this.blocks = blocks; this.versions = versions; this.materials = materials; this.decoder = decoder; }

    #region Worker capture
    /// <summary>Rejects changed or replaced chunks after copying; absent chunks never produce empty snapshots.</summary>
    public ValueTask<IChunkSnapshot?> TryCreateSnapshotAsync(ChunkKey key, int expectedVersion, CancellationToken ct)
    {
        var solid = ArrayPool<int>.Shared.Rent(32768); var fluid = ArrayPool<int>.Shared.Rent(32768);
        var light = ArrayPool<uint>.Shared.Rent(32768);
        TraceGeometryVoxel[]? output = ArrayPool<TraceGeometryVoxel>.Shared.Rent(32768);
        try
        {
            ct.ThrowIfCancellationRequested();
            key.Decode(out int cx, out int cy, out int cz);
            var origin = new BlockPos(cx * 32, cy * 32, cz * 32);
            var chunk = accessor.GetChunkAtBlockPos(origin);
            if (chunk == null || chunk.Disposed || versions.GetCurrentVersion(key) != expectedVersion) return ValueTask.FromResult<IChunkSnapshot?>(null);
            NearFieldChunkBulkReader.Copy(chunk, solid.AsSpan(0, 32768), fluid.AsSpan(0, 32768), light.AsSpan(0, 32768), ct);
            var position = new BlockPos(0);
            var decoded = new Dictionary<uint, (uint Normalized, uint Legacy)>();
            var indices = new Dictionary<int, uint>();
            for (int i = 0; i < 32768; i++)
            {
                if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                if (!decoded.TryGetValue(light[i], out var lighting))
                {
                    Vector4 value = decoder.Decode(light[i]);
                    uint normalized = TraceGeometryVoxel.PackLight(value);
                    // The legacy accessor returns low-byte red (HsvToRgba), even though its registry
                    // interprets the high byte as red. Preserve that existing shading convention here.
                    int rgb = (int)(normalized & 0x00FFFFFF);
                    uint blockLevel = (light[i] >> 5) & 31;
                    uint legacy = LumonSceneOccupancyPacking.Pack(blockLevel, light[i] & 31,
                        blockLevel == 0 ? 0 : materials.ResolveLight(rgb), 0);
                    lighting = (normalized, legacy); decoded.Add(light[i], lighting);
                }
                Block f = blocks(fluid[i]);
                Block block = f.Id != 0 && f.SideSolid.Any ? f : blocks(solid[i]);
                uint kind = 1, material = 0;
                if (block.Id != 0)
                {
                    if (!indices.TryGetValue(block.Id, out material)) indices.Add(block.Id, material = materials.Resolve(block));
                    position.Set(cx * 32 + (i & 31), cy * 32 + (i >> 10), cz * 32 + ((i >> 5) & 31));
                    var boxes = block.GetCollisionBoxes(accessor, position);
                    kind = block.RenderPass == EnumChunkRenderPass.Opaque && block.AllSidesOpaque && boxes is { Length: 1 } &&
                        boxes[0].MinX == 0 && boxes[0].MinY == 0 && boxes[0].MinZ == 0 &&
                        boxes[0].MaxX == 1 && boxes[0].MaxY == 1 && boxes[0].MaxZ == 1 ? 2u : 3u;
                }
                output[i] = new(kind | material << 2, lighting.Legacy | material << 18, lighting.Normalized);
            }
            if (chunk.Disposed || versions.GetCurrentVersion(key) != expectedVersion || !ReferenceEquals(chunk, accessor.GetChunkAtBlockPos(origin)))
                return ValueTask.FromResult<IChunkSnapshot?>(null);
            IChunkSnapshot snapshot = new PooledChunkSnapshot<TraceGeometryVoxel>(key, expectedVersion, 32, 32, 32, output, 32768);
            output = null; return ValueTask.FromResult<IChunkSnapshot?>(snapshot);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(solid); ArrayPool<int>.Shared.Return(fluid); ArrayPool<uint>.Shared.Return(light);
            if (output != null) ArrayPool<TraceGeometryVoxel>.Shared.Return(output);
        }
    }
    #endregion
}
