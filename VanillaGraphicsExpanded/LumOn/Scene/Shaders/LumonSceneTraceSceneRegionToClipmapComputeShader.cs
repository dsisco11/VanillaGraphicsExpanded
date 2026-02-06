using System;
using System.Buffers.Binary;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal sealed class LumonSceneTraceSceneRegionToClipmapComputeShader : IDisposable
{
    public const string ShaderName = "lumonscene_trace_scene_region_to_clipmap";

    private const int ParamsUboBinding = GpuBindingRegistry.Ubo.Object; // VGE_UBO_OBJECT_BINDING
    private const int ParamsUboSizeBytes = 16 + (8 * 16) + (8 * 16); // uvec4 + ivec4[8] + ivec4[8]
    private const int OriginMinCellOffsetBytes = 16;
    private const int RingOffsetBytes = 16 + (8 * 16);

    private const int RegionPayloadWordsSsboBindingIndex = 0; // layout(std430, binding=0)
    private const int RegionUpdatesSsboBindingIndex = 1;      // layout(std430, binding=1)

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private GpuUniformBuffer? paramsUbo;

    private const int OccLevelImageBaseUnit = 0;     // layout(binding=0) uimage3D vge_occLevels[8]

    private readonly GpuComputePipeline pipeline;

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneTraceSceneRegionToClipmapComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

        // Params UBO (non-opaque uniforms) for this compute pipeline.
        paramsUbo = GpuUniformBuffer.Create(debugName: "LumOnScene.TraceRegion.ParamsUBO");
    }

    private void ApplyParamsUbo()
    {
        paramsUbo ??= GpuUniformBuffer.Create(debugName: "LumOnScene.TraceRegion.ParamsUBO");
        paramsUbo.UploadOrResize(paramsBytes, ParamsUboSizeBytes, growExponentially: false);
        paramsUbo.BindBase(ParamsUboBinding);
    }

    public static bool TryCreate(
        ICoreAPI api,
        out LumonSceneTraceSceneRegionToClipmapComputeShader? shader,
        out string infoLog,
        bool preferSpirv = true,
        string? debugName = null)
    {
        shader = null;

        if (!GpuComputePipeline.TryCreateFromAssets(
            api: api,
            shaderName: ShaderName,
            pipeline: out var pipeline,
            sourceCode: out _,
            infoLog: out infoLog,
            preferSpirv: preferSpirv,
            stageExtension: "csh",
            defines: null,
            debugName: debugName,
            log: api.Logger))
        {
            return false;
        }

        if (pipeline is null)
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + "[VGE] RegionToClipmap pipeline creation returned null.";
            return false;
        }

        shader = new LumonSceneTraceSceneRegionToClipmapComputeShader(pipeline);
        return true;
    }

    public IDisposable UseScope() => pipeline.UseScope();

    public void Use() => pipeline.Use();

    public void BindRegionPayloadWordsSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        ssbo.BindBase(RegionPayloadWordsSsboBindingIndex);
    }

    public void BindRegionUpdatesSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        ssbo.BindBase(RegionUpdatesSsboBindingIndex);
    }

    public void BindOccLevelImage(int level, GpuTexture occLevel, TextureAccess access = TextureAccess.WriteOnly)
    {
        if (occLevel is null) throw new ArgumentNullException(nameof(occLevel));
        if (level < 0 || level >= 8) throw new ArgumentOutOfRangeException(nameof(level));

        occLevel.BindImageUnit(
            unit: OccLevelImageBaseUnit + level,
            access: access,
            level: 0,
            layered: true,
            layer: 0,
            format: SizedInternalFormat.R32ui);
    }

    public uint RegionUpdateCount
    {
        set
        {
            UboPacking.WriteUVec4(paramsBytes, 0, value, GetCountsY(), GetCountsZ(), 0u);
            ApplyParamsUbo();
        }
    }

    public int Levels
    {
        set
        {
            UboPacking.WriteUVec4(paramsBytes, 0, GetCountsX(), unchecked((uint)value), GetCountsZ(), 0u);
            ApplyParamsUbo();
        }
    }

    public int Resolution
    {
        set
        {
            UboPacking.WriteUVec4(paramsBytes, 0, GetCountsX(), GetCountsY(), unchecked((uint)value), 0u);
            ApplyParamsUbo();
        }
    }

    public void SetOriginMinCell(int level, int x, int y, int z)
    {
        if (level < 0 || level >= 8) throw new ArgumentOutOfRangeException(nameof(level));
        UboPacking.WriteIVec4(paramsBytes, OriginMinCellOffsetBytes + level * 16, x, y, z, 0);
        ApplyParamsUbo();
    }

    public void SetRing(int level, int x, int y, int z)
    {
        if (level < 0 || level >= 8) throw new ArgumentOutOfRangeException(nameof(level));
        UboPacking.WriteIVec4(paramsBytes, RingOffsetBytes + level * 16, x, y, z, 0);
        ApplyParamsUbo();
    }

    private uint GetCountsX() => BinaryPrimitives.ReadUInt32LittleEndian(paramsBytes.AsSpan(0, 4));
    private uint GetCountsY() => BinaryPrimitives.ReadUInt32LittleEndian(paramsBytes.AsSpan(4, 4));
    private uint GetCountsZ() => BinaryPrimitives.ReadUInt32LittleEndian(paramsBytes.AsSpan(8, 4));

    public void Dispose()
    {
        try
        {
            paramsUbo?.Dispose();
            paramsUbo = null;
            pipeline.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VGE] Exception disposing RegionToClipmap compute shader: {ex}");
        }
    }
}
