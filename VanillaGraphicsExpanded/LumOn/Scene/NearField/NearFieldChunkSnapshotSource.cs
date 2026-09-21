using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Captures near-field data directly on the bounded chunk-processing workers.</summary>
internal sealed class NearFieldChunkSnapshotSource : IChunkSnapshotSource
{
    private readonly IBlockAccessor accessor;
    private readonly System.Func<int, Block> block;
    private readonly IChunkVersionProvider versions;
    private readonly NearFieldMaterialRegistry materials;
    private readonly NearFieldLightDecoder lighting;
    private long captureTicks, captureCount, peakTicks;
    public long CaptureCount => Interlocked.Read(ref captureCount);
    public double CaptureMilliseconds => Interlocked.Read(ref captureTicks) * 1000d / Stopwatch.Frequency;
    public double PeakCaptureMilliseconds => Interlocked.Read(ref peakTicks) * 1000d / Stopwatch.Frequency;

    /// <summary>Injects source services; capture never schedules work back onto the main thread.</summary>
    public NearFieldChunkSnapshotSource(IBlockAccessor accessor, System.Func<int, Block> block,
        IChunkVersionProvider versions, NearFieldMaterialRegistry materials, NearFieldLightDecoder lighting)
    {
        this.accessor = accessor; this.block = block; this.versions = versions;
        this.materials = materials; this.lighting = lighting;
    }

    #region Worker capture
    /// <summary>Copies source layers, evaluates supported geometry, and rejects superseded or unloaded snapshots.</summary>
    public ValueTask<IChunkSnapshot?> TryCreateSnapshotAsync(ChunkKey key, int expectedVersion, CancellationToken ct)
    {
        long started = Stopwatch.GetTimestamp();
        int[] solids = ArrayPool<int>.Shared.Rent(32768), fluids = ArrayPool<int>.Shared.Rent(32768);
        uint[] lights = ArrayPool<uint>.Shared.Rent(32768);
        NearFieldSourceCell[]? cells = ArrayPool<NearFieldSourceCell>.Shared.Rent(32768);
        try
        {
            ct.ThrowIfCancellationRequested();
            key.Decode(out int x, out int y, out int z);
            var origin = new BlockPos(x * 32, y * 32, z * 32);
            IWorldChunk? chunk = accessor.GetChunkAtBlockPos(origin);
            if (chunk == null || chunk.Disposed || versions.GetCurrentVersion(key) != expectedVersion)
                return ValueTask.FromResult<IChunkSnapshot?>(null);
            NearFieldChunkBulkReader.Copy(chunk, solids.AsSpan(0, 32768), fluids.AsSpan(0, 32768), lights.AsSpan(0, 32768), ct);
            var position = new BlockPos(0);
            var decoded = new Dictionary<uint, Vector4>();
            var materialIndices = new Dictionary<int, uint>();
            for (int i = 0; i < 32768; i++)
            {
                if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                position.Set(x * 32 + (i & 31), y * 32 + (i >> 10), z * 32 + ((i >> 5) & 31));
                if (!decoded.TryGetValue(lights[i], out Vector4 light)) decoded.Add(lights[i], light = lighting.Decode(lights[i]));
                // Match most-solid layer selection, including solid fluid-layer blocks.
                Block fluid = block(fluids[i]);
                Block selected = fluid.Id != 0 && fluid.SideSolid.Any ? fluid : block(solids[i]);
                cells[i] = NearFieldCellCapture.CaptureGeometry(accessor, selected, position, materials, light, materialIndices);
            }
            if (chunk.Disposed || versions.GetCurrentVersion(key) != expectedVersion ||
                !ReferenceEquals(chunk, accessor.GetChunkAtBlockPos(origin))) return ValueTask.FromResult<IChunkSnapshot?>(null);
            IChunkSnapshot snapshot = new PooledChunkSnapshot<NearFieldSourceCell>(key, expectedVersion, 32, 32, 32, cells, 32768);
            cells = null; // Ownership passes to the shared snapshot lease.
            return ValueTask.FromResult<IChunkSnapshot?>(snapshot);
        }
        catch (NotImplementedException) { return ValueTask.FromResult<IChunkSnapshot?>(null); }
        finally
        {
            ArrayPool<int>.Shared.Return(solids); ArrayPool<int>.Shared.Return(fluids); ArrayPool<uint>.Shared.Return(lights);
            if (cells != null) ArrayPool<NearFieldSourceCell>.Shared.Return(cells);
            long elapsed = Stopwatch.GetTimestamp() - started;
            Interlocked.Add(ref captureTicks, elapsed); Interlocked.Increment(ref captureCount);
            long previous;
            do { previous = Interlocked.Read(ref peakTicks); if (elapsed <= previous) break; }
            while (Interlocked.CompareExchange(ref peakTicks, elapsed, previous) != previous);
        }
    }
    #endregion
}
