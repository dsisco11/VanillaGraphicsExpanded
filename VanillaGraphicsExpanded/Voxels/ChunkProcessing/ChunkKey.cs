using System;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

/// <summary>
/// Stable key for identifying a voxel chunk in background processing systems.
/// </summary>
/// <remarks>
/// This key is intentionally compact and hash-friendly. It uses 21-bit ZigZag packing per axis
/// (matching existing chunk-key packing used elsewhere in this repo).
/// </remarks>
public readonly record struct ChunkKey(ulong Packed)
{
    public static ChunkKey FromChunkCoords(int chunkX, int chunkY, int chunkZ)
    {
        const ulong mask = (1ul << 21) - 1ul;

        static ulong ZigZag(int v) => (ulong)((v << 1) ^ (v >> 31));

        ulong x = ZigZag(chunkX) & mask;
        ulong y = ZigZag(chunkY) & mask;
        ulong z = ZigZag(chunkZ) & mask;

        return new ChunkKey(x | (y << 21) | (z << 42));
    }

    public void Decode(out int chunkX, out int chunkY, out int chunkZ)
    {
        const ulong mask = (1ul << 21) - 1ul;

        static int DecodeZigZag(ulong v)
            => (int)((v >> 1) ^ (ulong)-(long)(v & 1ul));

        chunkX = DecodeZigZag(Packed & mask);
        chunkY = DecodeZigZag((Packed >> 21) & mask);
        chunkZ = DecodeZigZag((Packed >> 42) & mask);
    }

    public override string ToString()
    {
        Decode(out int x, out int y, out int z);
        return $"({x},{y},{z})";
    }
}

