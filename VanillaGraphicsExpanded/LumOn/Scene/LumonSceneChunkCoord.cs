using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal readonly struct LumonSceneChunkCoord : IEquatable<LumonSceneChunkCoord>
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public LumonSceneChunkCoord(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public bool Equals(LumonSceneChunkCoord other)
        => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj)
        => obj is LumonSceneChunkCoord other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(X, Y, Z);

    public static bool operator ==(LumonSceneChunkCoord left, LumonSceneChunkCoord right)
        => left.Equals(right);

    public static bool operator !=(LumonSceneChunkCoord left, LumonSceneChunkCoord right)
        => !left.Equals(right);

    public override string ToString()
        => $"({X},{Y},{Z})";

    // 21-bit ZigZag packing per axis (matches WorldProbeModSystem chunk key packing).
    public ulong ToKey()
    {
        const ulong mask = (1ul << 21) - 1ul;
        static ulong ZigZag(int v) => (ulong)((v << 1) ^ (v >> 31));

        ulong x = ZigZag(X) & mask;
        ulong y = ZigZag(Y) & mask;
        ulong z = ZigZag(Z) & mask;
        return x | (y << 21) | (z << 42);
    }

    public static LumonSceneChunkCoord FromKey(ulong key)
    {
        const ulong mask = (1ul << 21) - 1ul;

        static int DecodeZigZag(ulong v)
            => (int)((v >> 1) ^ (ulong)-(long)(v & 1ul));

        int x = DecodeZigZag(key & mask);
        int y = DecodeZigZag((key >> 21) & mask);
        int z = DecodeZigZag((key >> 42) & mask);
        return new LumonSceneChunkCoord(x, y, z);
    }
}

