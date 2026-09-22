using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.Buffers.Binary;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the compute shader contract and dispatch resources for this scene operation.</summary>
[ShaderProgram("Contract", "lumonscene_relight_voxel_dda", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_relight_voxel_dda.csh")]
internal sealed partial class LumonSceneRelightVoxelDdaComputeShader : IDisposable
{

    public static string ShaderName => Contract.Identity;

    private const int ParamsUboBinding = GpuBindingRegistry.Ubo.Object; // VGE_UBO_OBJECT_BINDING
    private const int ParamsUboSizeBytes = 80; // uvec4 + uvec4 + ivec4 + ivec4 + ivec4

    private const int DepthAtlasSamplerUnit = 0;
    private const int MaterialAtlasSamplerUnit = 1;
    private const int LightColorLutSamplerUnit = 3;
    private const int BlockLevelScalarLutSamplerUnit = 4;
    private const int SunLevelScalarLutSamplerUnit = 5;
    private const int SurfaceLutSamplerUnit = 7;

    private const int IrradianceAtlasImageUnit = 0; // layout(binding=0, rgba16f)

    private const int RelightWorkSsboBindingIndex = 0; // layout(std430, binding=0)
    private const int PatchMetaSsboBindingIndex = 1;   // layout(std430, binding=1)

    private const int DebugCountersBindingIndex = 0;   // layout(binding=0, offset=...)

    private const int AtlasLayoutOffsetBytes = 0;
    private const int RelightUints0OffsetBytes = 16;
    private const int RelightInts0OffsetBytes = 32;
    private const int OccOriginMinCell0OffsetBytes = 48;
    private const int OccRing0OffsetBytes = 64;

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private GpuUniformBuffer? paramsUbo;

    private readonly GpuComputePipeline pipeline;
    private readonly Geometry.TraceGeometryComputeBindings sharedGeometry = new();

    /// <summary>Binds the shared geometry and independent logical domains.</summary>
    public void BindSharedGeometry(Geometry.TraceGeometryGpuScene? scene) => sharedGeometry.Bind(scene);

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneRelightVoxelDdaComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

        paramsUbo = GpuUniformBuffer.Create(debugName: "LumOnScene.Relight.ParamsUBO");
    }

    private void ApplyParamsUbo()
    {
        paramsUbo ??= GpuUniformBuffer.Create(debugName: "LumOnScene.Relight.ParamsUBO");
        paramsUbo.UploadOrResize(paramsBytes, ParamsUboSizeBytes, growExponentially: false);
        paramsUbo.BindBase(ParamsUboBinding);
    }

    private uint ReadU32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(paramsBytes.AsSpan(offset, 4));
    private int ReadI32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(paramsBytes.AsSpan(offset, 4));

    public static bool TryCreate(
        ICoreAPI api,
        out LumonSceneRelightVoxelDdaComputeShader? shader,
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
            log: api.Logger,
            layout: new LumonSceneComputeProgramLayouts.RelightVoxelDda()))
        {
            return false;
        }

        if (pipeline is null)
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + "[VGE] RelightVoxelDda pipeline creation returned null.";
            return false;
        }

        shader = new LumonSceneRelightVoxelDdaComputeShader(pipeline);
        return true;
    }

    public IDisposable UseScope() => pipeline.UseScope();

    public void Use() => pipeline.Use();

    public void BindTerrainBridgeUbo(GpuUniformBuffer? ubo)
    {
        // Contract is defined in lumon_terrain_bridge_ubo.glsl: LUMON_UBO_TERRAIN_BRIDGE_BINDING=27
        ubo?.BindBase(LumOnTerrainBridgeUboState.Binding);
    }

    public void BindRelightWorkSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        ssbo.BindBase(RelightWorkSsboBindingIndex);
    }

    public void BindPatchMetaSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        ssbo.BindBase(PatchMetaSsboBindingIndex);
    }

    public void BindDepthAtlas(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2DArray, unit: DepthAtlasSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: DepthAtlasSamplerUnit);
    }

    public void BindMaterialAtlas(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2DArray, unit: MaterialAtlasSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: MaterialAtlasSamplerUnit);
    }

public void BindLightColorLut(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit: LightColorLutSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: LightColorLutSamplerUnit);
    }

    public void BindBlockLevelScalarLut(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit: BlockLevelScalarLutSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: BlockLevelScalarLutSamplerUnit);
    }

    public void BindSunLevelScalarLut(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit: SunLevelScalarLutSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: SunLevelScalarLutSamplerUnit);
    }

public void BindSurfaceLut(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit: SurfaceLutSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: SurfaceLutSamplerUnit);
    }

    public void BindIrradianceAtlasImage(GpuTexture irradianceAtlas, TextureAccess access = TextureAccess.ReadWrite)
    {
        if (irradianceAtlas is null) throw new ArgumentNullException(nameof(irradianceAtlas));

        irradianceAtlas.BindImageUnit(
            unit: IrradianceAtlasImageUnit,
            access: access,
            level: 0,
            layered: true,
            layer: 0,
            format: SizedInternalFormat.Rgba16f);
    }

    public void BindDebugCounters(GpuAtomicCounterBuffer? debugCounters)
    {
        debugCounters?.BindBase(DebugCountersBindingIndex);
    }

    public void SetAtlasLayout(uint tileSizeTexels, uint tilesPerAxis, uint tilesPerAtlas, uint borderTexels)
    {
        UboPacking.WriteUVec4(paramsBytes, AtlasLayoutOffsetBytes, tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels);
        ApplyParamsUbo();
    }

    public void SetRelightParams(int frameIndex, uint texelsPerPagePerFrame, uint raysPerTexel, uint maxDdaSteps, bool debugCountersEnabled)
    {
        UboPacking.WriteUVec4(
            paramsBytes,
            RelightUints0OffsetBytes,
            texelsPerPagePerFrame,
            raysPerTexel,
            maxDdaSteps,
            debugCountersEnabled ? 1u : 0u);

        int occResolution = ReadI32(RelightInts0OffsetBytes + 4);
        UboPacking.WriteIVec4(paramsBytes, RelightInts0OffsetBytes, frameIndex, occResolution, 0, 0);
        ApplyParamsUbo();
    }

    public void SetOccupancyMapping(int originMinCellX, int originMinCellY, int originMinCellZ, int ringX, int ringY, int ringZ, int resolution)
    {
        UboPacking.WriteIVec4(paramsBytes, OccOriginMinCell0OffsetBytes, originMinCellX, originMinCellY, originMinCellZ, 0);
        UboPacking.WriteIVec4(paramsBytes, OccRing0OffsetBytes, ringX, ringY, ringZ, 0);

        int frameIndex = ReadI32(RelightInts0OffsetBytes);
        UboPacking.WriteIVec4(paramsBytes, RelightInts0OffsetBytes, frameIndex, resolution, 0, 0);
        ApplyParamsUbo();
    }

    public void DispatchBound(int numGroupsX, int numGroupsY, int numGroupsZ) => pipeline.DispatchBound(numGroupsX, numGroupsY, numGroupsZ);

    public void Dispose()
    {
        try
        {
            sharedGeometry.Dispose();
            paramsUbo?.Dispose();
            paramsUbo = null;
            pipeline.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VGE] Exception disposing RelightVoxelDda compute shader: {ex}");
        }
    }
}
