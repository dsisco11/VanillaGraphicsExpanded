using System;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 23.3: Packed TraceScene payload artifact for one 32^3 world-cell region.
/// </summary>
internal sealed class LumonSceneTraceSceneRegionArtifact : IArtifactSizeInfo
{
    public ChunkKey Key { get; }
    public int Version { get; }

    /// <summary>
    /// Region coordinate in 32^3 units (same values as <see cref="ChunkKey"/> decoded coordinates).
    /// </summary>
    public VectorInt3 RegionCoord { get; }

    /// <summary>
    /// Packed occupancy payload words (length == 32^3). Format matches <see cref="LumonSceneOccupancyPacking"/>.
    /// </summary>
    public uint[] PayloadWords { get; }

    public long EstimatedBytes => (long)PayloadWords.Length * sizeof(uint);

    public LumonSceneTraceSceneRegionArtifact(ChunkKey key, int version, in VectorInt3 regionCoord, uint[] payloadWords)
    {
        if (payloadWords is null) throw new ArgumentNullException(nameof(payloadWords));
        if (payloadWords.Length != LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount)
        {
            throw new ArgumentException(
                $"Expected payload length {LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount} (32^3), got {payloadWords.Length}.",
                nameof(payloadWords));
        }

        Key = key;
        Version = version;
        RegionCoord = regionCoord;
        PayloadWords = payloadWords;
    }
}

