using VanillaGraphicsExpanded.Rendering.RHI;
using System.Threading;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Owns the terrain slot mapping and its GPU uniform-buffer representation.</summary>
internal static class LumonSceneChunkSlotUniformState
{
    public const string BlockName = "VgeLumonSceneChunkSlotParamsUBO";
    public const int Binding = GpuBindingRegistry.Ubo.Object;
    private static readonly LumonSceneChunkSlotParameters parameterValues = new();
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

    public static GpuTextureRef GenerationTexture { get; private set; }
    /// <summary>Retains the mapping owner's sampled declaration until its selected texture changes.</summary>
    internal static RHISampledTextureBindings? GenerationBindings { get; private set; }

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
        GenerationTexture = default;
        GenerationBindings = null;
        Interlocked.Increment(ref version);
    }

    /// <summary>Publishes CPU mapping state; GPU upload is deferred until a terrain program uses it.</summary>
    public static void Update(VectorInt3 originMinChunk, VectorInt3 dims, VectorInt3 ring, GpuTextureRef generationTexture)
    {
        OriginMinChunk = originMinChunk;
        Dims = dims;
        Ring = ring;
        if (GenerationTexture != generationTexture || GenerationBindings is null)
            GenerationBindings = [new(GenerationTextureUnit, generationTexture)];
        GenerationTexture = generationTexture;
        Enabled = !generationTexture.IsEmpty && dims.X > 0 && dims.Y > 0 && dims.Z > 0;
        Interlocked.Increment(ref version);
    }
    #endregion

    #region Terrain binding
    /// <summary>Uploads changed mapping parameters and restores the global binding on every terrain use.</summary>
    internal static void BindParameters()
    {
        parameters ??= RHICommandContext.Current.CreateUniformBuffer(new RHIBufferDescription { Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "LumonScene.ChunkSlotParameters" }).RequireResource();
        if (uploadedVersion != Version)
        {
            parameterValues.Set(OriginMinChunk, Dims, Ring);
            parameters.UploadOrResize(parameterValues.Bytes, growExponentially: false);
            uploadedVersion = Version;
        }
        parameters.BindBase(Binding);
    }
    #endregion
}
