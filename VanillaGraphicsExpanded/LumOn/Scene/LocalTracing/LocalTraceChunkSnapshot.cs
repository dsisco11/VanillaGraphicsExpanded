using System;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;

/// <summary>Immutable owned copy of a completed 32-block source chunk.</summary>
internal sealed class LocalTraceChunkSnapshot : IPartitionSnapshot, IArtifactSizeInfo
{
    private readonly LocalTraceSourceCell[] cells;
    public ChunkKey Key { get; }
    public int Version { get; }
    public long EstimatedBytes => (long)cells.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<LocalTraceSourceCell>();

    /// <summary>Copies source data so pooled snapshot disposal cannot change a pending publication.</summary>
    public LocalTraceChunkSnapshot(ChunkKey key, int version, ReadOnlySpan<LocalTraceSourceCell> source)
    {
        if (source.Length != 32768) throw new ArgumentException("Expected a 32-block source chunk.");
        Key = key; Version = version; cells = source.ToArray();
    }

    /// <summary>Extracts one of the eight fixed-zero 16-block subcells in X/Z/Y order.</summary>
    public LocalTraceCellContent Extract(in PartitionCoordinate coordinate)
    {
        Key.Decode(out int chunkX, out int chunkY, out int chunkZ);
        if ((coordinate.X >> 1) != chunkX || (coordinate.Y >> 1) != chunkY || (coordinate.Z >> 1) != chunkZ)
            throw new ArgumentException("Cell does not belong to this source chunk.");
        int ox = (int)(coordinate.X & 1) * 16, oy = (int)(coordinate.Y & 1) * 16, oz = (int)(coordinate.Z & 1) * 16;
        var result = new LocalTraceSourceCell[4096];
        bool unsupported = false;
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        for (int x = 0; x < 16; x++)
        {
            LocalTraceSourceCell cell = cells[((y + oy) * 32 + z + oz) * 32 + x + ox];
            result[(y * 16 + z) * 16 + x] = cell;
            unsupported |= (cell.Geometry & 3) is not (1 or 2);
        }
        return new(result, unsupported);
    }
}
