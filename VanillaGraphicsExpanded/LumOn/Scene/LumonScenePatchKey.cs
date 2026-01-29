using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal enum LumonScenePatchKeyKind : byte
{
    VoxelFacePatch = 0,
    MeshCard = 1,
}

internal readonly struct LumonScenePatchKey : IEquatable<LumonScenePatchKey>
{
    public readonly LumonScenePatchKeyKind Kind;
    private readonly ulong a;
    private readonly ulong b;

    private LumonScenePatchKey(LumonScenePatchKeyKind kind, ulong a, ulong b)
    {
        Kind = kind;
        this.a = a;
        this.b = b;
    }

    public static LumonScenePatchKey CreateVoxelFacePatch(
        LumonSceneFace face,
        byte planeIndex,
        byte patchUIndex,
        byte patchVIndex)
    {
        if ((byte)face > 5) throw new ArgumentOutOfRangeException(nameof(face));
        if (planeIndex > 31) throw new ArgumentOutOfRangeException(nameof(planeIndex), "planeIndex must be in [0, 31]");
        if (patchUIndex > 7) throw new ArgumentOutOfRangeException(nameof(patchUIndex), "patchUIndex must be in [0, 7]");
        if (patchVIndex > 7) throw new ArgumentOutOfRangeException(nameof(patchVIndex), "patchVIndex must be in [0, 7]");

        uint packed =
            ((uint)face & 0x7u) |
            (((uint)planeIndex & 0x1Fu) << 3) |
            (((uint)patchUIndex & 0x7u) << 8) |
            (((uint)patchVIndex & 0x7u) << 11);

        return new LumonScenePatchKey(LumonScenePatchKeyKind.VoxelFacePatch, packed, 0);
    }

    public static LumonScenePatchKey CreateMeshCard(
        ulong instanceStableId,
        ushort cardIndex)
    {
        return new LumonScenePatchKey(LumonScenePatchKeyKind.MeshCard, instanceStableId, cardIndex);
    }

    public bool Equals(LumonScenePatchKey other)
        => Kind == other.Kind && a == other.a && b == other.b;

    public override bool Equals(object? obj)
        => obj is LumonScenePatchKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine((int)Kind, a, b);

    public static bool operator ==(LumonScenePatchKey left, LumonScenePatchKey right)
        => left.Equals(right);

    public static bool operator !=(LumonScenePatchKey left, LumonScenePatchKey right)
        => !left.Equals(right);

    public override string ToString()
        => Kind switch
        {
            LumonScenePatchKeyKind.VoxelFacePatch => $"VoxelFacePatch:0x{a:x}",
            LumonScenePatchKeyKind.MeshCard => $"MeshCard:inst=0x{a:x16},card={b}",
            _ => $"Kind={(int)Kind},a=0x{a:x},b=0x{b:x}"
        };
}
