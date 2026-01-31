using System.Threading;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal static class LumonSceneChunkSlotUniformState
{
    public const int GenerationTextureUnit = 13;

    public const string OriginMinChunkUniform = "vge_lumonSceneChunkSlotOriginMinChunk";
    public const string DimsUniform = "vge_lumonSceneChunkSlotDims";
    public const string RingUniform = "vge_lumonSceneChunkSlotRing";
    public const string GenerationSamplerUniform = "vge_lumonSceneChunkSlotGenerationTex";

    private static int version;

    public static int Version => Volatile.Read(ref version);
    public static bool Enabled { get; private set; }

    public static VectorInt3 OriginMinChunk { get; private set; }
    public static VectorInt3 Dims { get; private set; }
    public static VectorInt3 Ring { get; private set; }

    public static int GenerationTextureId { get; private set; }

    public static void Disable()
    {
        Enabled = false;
        OriginMinChunk = default;
        Dims = default;
        Ring = default;
        GenerationTextureId = 0;
        Interlocked.Increment(ref version);
    }

    public static void Update(VectorInt3 originMinChunk, VectorInt3 dims, VectorInt3 ring, int generationTextureId)
    {
        OriginMinChunk = originMinChunk;
        Dims = dims;
        Ring = ring;
        GenerationTextureId = generationTextureId;
        Enabled = generationTextureId != 0 && dims.X > 0 && dims.Y > 0 && dims.Z > 0;
        Interlocked.Increment(ref version);
    }
}

