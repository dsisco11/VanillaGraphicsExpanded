using System.Threading;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Owns the terrain slot mapping and its GPU uniform-buffer representation.</summary>
internal static class LumonSceneChunkSlotUniformState
{
    public const string BlockName = "VgeLumonSceneChunkSlotParamsUBO";
    public const int Binding = GpuBindingRegistry.Ubo.Object;
    private static readonly byte[] parameterBytes = new byte[48];
    private static GpuUniformBuffer? parameters;
    private static int uploadedVersion = -1;
    public const int GenerationTextureUnit = 13;

    public const string GenerationSamplerUniform = "vge_lumonSceneChunkSlotGenerationTex";

    private static int version;

    public static int Version => Volatile.Read(ref version);
    public static bool Enabled { get; private set; }

    public static VectorInt3 OriginMinChunk { get; private set; }
    public static VectorInt3 Dims { get; private set; }
    public static VectorInt3 Ring { get; private set; }

    public static int GenerationTextureId { get; private set; }

    #region Mapping lifecycle
    /// <summary>Clears the mapping and releases its render-thread GPU storage.</summary>
    public static void Disable()
    {
        parameters?.Dispose();
        parameters = null;
        uploadedVersion = -1;
        Enabled = false;
        OriginMinChunk = default;
        Dims = default;
        Ring = default;
        GenerationTextureId = 0;
        Interlocked.Increment(ref version);
    }

    /// <summary>Publishes CPU mapping state; GPU upload is deferred until a terrain program uses it.</summary>
    public static void Update(VectorInt3 originMinChunk, VectorInt3 dims, VectorInt3 ring, int generationTextureId)
    {
        OriginMinChunk = originMinChunk;
        Dims = dims;
        Ring = ring;
        GenerationTextureId = generationTextureId;
        Enabled = generationTextureId != 0 && dims.X > 0 && dims.Y > 0 && dims.Z > 0;
        Interlocked.Increment(ref version);
    }
    #endregion

    #region Terrain binding
    /// <summary>Uploads changed mapping parameters and restores the global binding on every terrain use.</summary>
    internal static void BindParameters()
    {
        parameters ??= GpuUniformBuffer.Create(debugName: "LumonScene.ChunkSlotParameters");
        if (uploadedVersion != Version)
        {
            // Three std140 ivec4 lanes match lumonscene_chunkslot_params_ubo.glsl.
            UboPacking.WriteIVec4(parameterBytes, 0, OriginMinChunk.X, OriginMinChunk.Y, OriginMinChunk.Z, 0);
            UboPacking.WriteIVec4(parameterBytes, 16, Dims.X, Dims.Y, Dims.Z, 0);
            UboPacking.WriteIVec4(parameterBytes, 32, Ring.X, Ring.Y, Ring.Z, 0);
            parameters.UploadOrResize(parameterBytes, growExponentially: false);
            uploadedVersion = Version;
        }
        parameters.BindBase(Binding);
    }
    #endregion
}
