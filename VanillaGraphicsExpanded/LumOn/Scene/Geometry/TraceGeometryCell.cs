using System;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Detached native upload arrays; only the publication backend can read their spans.</summary>
internal sealed class TraceGeometryCell : IPartitionContent
{
    private readonly uint[] geometry, legacy;
    private readonly byte[] light;
    public ReadOnlySpan<uint> Geometry => geometry;
    public ReadOnlySpan<uint> Legacy => legacy;
    public ReadOnlySpan<byte> Light => light;
    public bool Unsupported { get; }
    public const long UploadBytes = 4096L * TraceGeometryVoxel.Bytes;

    /// <summary>Copies constructor buffers to enforce immutability across publication queues.</summary>
    public TraceGeometryCell(ReadOnlySpan<uint> geometry, ReadOnlySpan<uint> legacy, ReadOnlySpan<byte> light, bool unsupported)
    {
        if (geometry.Length != 4096 || legacy.Length != 4096 || light.Length != 16384) throw new ArgumentException("Wrong publication size.");
        this.geometry = geometry.ToArray(); this.legacy = legacy.ToArray(); this.light = light.ToArray(); Unsupported = unsupported;
    }
}

