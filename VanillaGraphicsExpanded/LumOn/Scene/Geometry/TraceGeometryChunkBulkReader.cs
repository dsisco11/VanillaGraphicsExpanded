using System;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Copies chunk-local block layers and packed lighting while holding each engine layer's read lock.</summary>
internal static class TraceGeometryChunkBulkReader
{
    #region Snapshot capture
    /// <summary>Avoids world-coordinate lookups and per-voxel lock acquisition on engine chunks.</summary>
    public static void Copy(IWorldChunk chunk, Span<int> solids, Span<int> fluids, Span<uint> lights, CancellationToken cancellation)
    {
        chunk.Unpack_ReadOnly();
        if (chunk.Disposed) throw new OperationCanceledException("Source chunk was unloaded.");
        if (chunk.Data is ChunkData data)
        {
            // Retain exact layer identities; packing/replacement must not redirect unlocks
            // to different layers. Do not call the shared bulk-lock scratch-state API.
            CopyLayer(data.blocksLayer, solids, cancellation);
            CopyLayer(data.fluidsLayer, fluids, cancellation);
            ChunkDataLayer? light = data.lightLayer;
            if (light == null) { lights.Clear(); return; }
            light.readWriteLock.AcquireReadLock();
            try
            {
                for (int i = 0; i < lights.Length; i++)
                {
                    if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
                    lights[i] = (uint)light.GetUnsafe_PaletteCheck(i);
                }
            }
            finally { light.readWriteLock.ReleaseReadLock(); }
            return;
        }
        // Alternate implementations retain their API's synchronization. They still avoid
        // a world chunk lookup and Vec4 allocation for each light sample.
        for (int i = 0; i < solids.Length; i++)
        {
            if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
            solids[i] = chunk.Data.GetBlockId(i, BlockLayersAccess.Solid);
            fluids[i] = chunk.Data.GetFluid(i);
            uint packed = chunk.Unpack_AndReadLight(i, out int saturation);
            lights[i] = packed | ((uint)saturation << 16);
        }
    }

    /// <summary>Copies one retained palette under one read lock and always releases it on cancellation.</summary>
    private static void CopyLayer(ChunkDataLayer? layer, Span<int> destination, CancellationToken cancellation)
    {
        if (layer == null) { destination.Clear(); return; }
        layer.readWriteLock.AcquireReadLock();
        try
        {
            for (int i = 0; i < destination.Length; i++)
            {
                if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
                destination[i] = layer.GetUnsafe_PaletteCheck(i);
            }
        }
        finally { layer.readWriteLock.ReleaseReadLock(); }
    }
    #endregion
}
