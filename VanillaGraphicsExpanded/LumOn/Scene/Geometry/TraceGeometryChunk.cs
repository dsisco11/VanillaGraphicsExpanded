using System;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Immutable detached source revision shared by all dependent publication cells.</summary>
internal sealed class TraceGeometryChunk : IPartitionSnapshot, IArtifactSizeInfo
{
    private readonly TraceGeometryVoxel[] voxels;
    public ChunkKey Key { get; }
    public int Version { get; }
    public long EstimatedBytes => 32768L * TraceGeometryVoxel.Bytes;

    /// <summary>Owns a copy that cannot be changed by pooled source disposal.</summary>
    public TraceGeometryChunk(ChunkKey key, int version, ReadOnlySpan<TraceGeometryVoxel> cells)
    {
        if (cells.Length != 32768) throw new ArgumentException("Expected 32 cubed voxels.");
        Key = key; Version = version; voxels = cells.ToArray();
    }

    /// <summary>Extracts an immutable cell, converting source X/Z/Y into GPU X/Y/Z order without further world reads.</summary>
    public TraceGeometryCell Extract(in PartitionCoordinate coordinate)
    {
        Key.Decode(out int cx, out int cy, out int cz);
        if ((coordinate.X >> 1) != cx || (coordinate.Y >> 1) != cy || (coordinate.Z >> 1) != cz) throw new ArgumentException("Wrong source chunk.");
        int ox = (int)(coordinate.X & 1) * 16, oy = (int)(coordinate.Y & 1) * 16, oz = (int)(coordinate.Z & 1) * 16;
        var geometry = new uint[4096]; var legacy = new uint[4096]; var light = new byte[16384];
        bool unsupported = false;
        for (int z = 0; z < 16; z++)
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            var voxel = voxels[((y + oy) * 32 + z + oz) * 32 + x + ox];
            int i = (z * 16 + y) * 16 + x;
            geometry[i] = voxel.Geometry; legacy[i] = voxel.LegacyLight;
            for (int c = 0; c < 4; c++) light[i * 4 + c] = (byte)(voxel.NormalizedLight >> (8 * c));
            unsupported |= voxel.Kind is 0 or 3;
        }
        return new(geometry, legacy, light, unsupported);
    }
}


