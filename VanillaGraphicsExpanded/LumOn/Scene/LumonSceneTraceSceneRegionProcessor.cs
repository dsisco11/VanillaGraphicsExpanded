using System;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 23.3: Chunk-processing job that produces a packed TraceScene payload (R32UI words) for a 32^3 region.
/// </summary>
/// <remarks>
/// Snapshot contract:
/// - SizeX/Y/Z must be 32.
/// - Snapshot voxel element type must be either:
///   - <see cref="uint"/>: already-packed payload words (one per cell), OR
///   - <see cref="LumonSceneTraceSceneSourceCell"/>: source data that this processor packs into payload words.
/// </remarks>
internal sealed class LumonSceneTraceSceneRegionProcessor : IChunkProcessor<LumonSceneTraceSceneRegionArtifact>
{
    public const string ProcessorId = "LumOn.TraceScene.RegionPayload.R32ui";

    public string Id => ProcessorId;

    public ValueTask<LumonSceneTraceSceneRegionArtifact> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        if (snapshot.SizeX != LumonSceneTraceSceneClipmapMath.RegionSize
            || snapshot.SizeY != LumonSceneTraceSceneClipmapMath.RegionSize
            || snapshot.SizeZ != LumonSceneTraceSceneClipmapMath.RegionSize)
        {
            throw new InvalidOperationException(
                $"TraceScene region snapshot must be 32^3, got {snapshot.SizeX}x{snapshot.SizeY}x{snapshot.SizeZ}.");
        }

        snapshot.Key.Decode(out int rx, out int ry, out int rz);
        var regionCoord = new VectorInt3(rx, ry, rz);

        // Fast-path: snapshot already contains packed payload words.
        if (snapshot is PooledChunkSnapshot<uint> packed)
        {
            ReadOnlySpan<uint> src = packed.Voxels.Span;
            if (src.Length < LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount)
            {
                throw new InvalidOperationException(
                    $"Packed payload snapshot too small: {src.Length} words (expected {LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount}).");
            }

            uint[] dst = new uint[LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount];
            src[..dst.Length].CopyTo(dst);
            return ValueTask.FromResult(new LumonSceneTraceSceneRegionArtifact(snapshot.Key, snapshot.Version, regionCoord, dst));
        }

        // Source-cell path: pack into payload words.
        if (snapshot is PooledChunkSnapshot<LumonSceneTraceSceneSourceCell> srcSnapshot)
        {
            ReadOnlySpan<LumonSceneTraceSceneSourceCell> src = srcSnapshot.Voxels.Span;
            if (src.Length < LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount)
            {
                throw new InvalidOperationException(
                    $"Source-cell snapshot too small: {src.Length} cells (expected {LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount}).");
            }

            uint[] dst = new uint[LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount];

            for (int i = 0; i < dst.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var c = src[i];
                if (c.IsSolid == 0)
                {
                    dst[i] = 0u;
                    continue;
                }

                dst[i] = LumonSceneOccupancyPacking.Pack(
                    blockLevel: c.BlockLevel,
                    sunLevel: c.SunLevel,
                    lightId: c.LightId,
                    materialPaletteIndex: c.MaterialPaletteIndex);
            }

            return ValueTask.FromResult(new LumonSceneTraceSceneRegionArtifact(snapshot.Key, snapshot.Version, regionCoord, dst));
        }

        throw new InvalidOperationException(
            $"Unsupported snapshot type {snapshot.GetType().FullName} for {nameof(LumonSceneTraceSceneRegionProcessor)}. " +
            $"Expected PooledChunkSnapshot<uint> or PooledChunkSnapshot<{nameof(LumonSceneTraceSceneSourceCell)}>.");
    }
}

