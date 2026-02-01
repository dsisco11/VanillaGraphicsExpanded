using System.Threading;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Provides a stable mapping between Vintage Story's "matrix space" (camera-relative / origin-shifted)
/// positions used by terrain shaders and world chunk coordinates used by LumonScene chunk-slot mapping.
/// </summary>
internal static class LumonSceneWorldCoordUniformState
{
    public const string WorldChunkCoordOffsetUniform = "vge_lumonSceneWorldChunkCoordOffset";
    public const string WorldBlockOffsetRemUniform = "vge_lumonSceneWorldBlockOffsetRem";

    private static int version;

    public static int Version => Volatile.Read(ref version);

    public static VectorInt3 WorldChunkCoordOffset { get; private set; }
    public static Vector3d WorldBlockOffsetRem { get; private set; }

    public static void Disable()
    {
        WorldChunkCoordOffset = default;
        WorldBlockOffsetRem = default;
        Interlocked.Increment(ref version);
    }

    public static void Update(VectorInt3 worldChunkCoordOffset, Vector3d worldBlockOffsetRem)
    {
        WorldChunkCoordOffset = worldChunkCoordOffset;
        WorldBlockOffsetRem = worldBlockOffsetRem;
        Interlocked.Increment(ref version);
    }
}

