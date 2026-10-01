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
internal sealed partial class LumonSceneRelightVoxelDdaComputeShader : TraceGeometryComputeShader, ILumonSceneRelightVoxelDdaComputeShaderBindings
{

    public static string ShaderName => Contract.Identity;
    private const int ParamsUboSizeBytes = 80; // uvec4 + uvec4 + ivec4 + ivec4 + ivec4

    private const int AtlasLayoutOffsetBytes = 0;
    private const int RelightUints0OffsetBytes = 16;
    private const int RelightInts0OffsetBytes = 32;
    private const int OccOriginMinCell0OffsetBytes = 48;
    private const int OccRing0OffsetBytes = 64;

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private readonly PackedUniformBuffer parameters = new(ParamsUboSizeBytes);

    #region Public API
    /// <summary>Creates the executable before adopting its retained input owner.</summary>
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

    /// <summary>Retains TerrainBridgeUbo for the next dispatch submission.</summary>
    public void BindTerrainBridgeUbo(GpuUniformBuffer? ubo)
    { TerrainBridge = ubo; }

    /// <summary>Retains RelightWorkSsbo for the next dispatch submission.</summary>
    public void BindRelightWorkSsbo(GpuShaderStorageBuffer ssbo)
    { RelightWork = ssbo; }

    /// <summary>Retains PatchMetaSsbo for the next dispatch submission.</summary>
    public void BindPatchMetaSsbo(GpuShaderStorageBuffer ssbo)
    { PatchMetadata = ssbo; }

    /// <summary>Retains DepthAtlas for the next dispatch submission.</summary>
    public void BindDepthAtlas(int textureId)
    { DepthAtlas = textureId; }

    /// <summary>Retains MaterialAtlas for the next dispatch submission.</summary>
    public void BindMaterialAtlas(int textureId)
    { MaterialAtlas = textureId; }

    /// <summary>Retains the light-color lookup texture until dispatch.</summary>
    public void BindLightColorLut(int textureId)
    { LightColorLut = textureId; }

    /// <summary>Retains BlockLevelScalarLut for the next dispatch submission.</summary>
    public void BindBlockLevelScalarLut(int textureId)
    { BlockLevelScalarLut = textureId; }

    /// <summary>Retains SunLevelScalarLut for the next dispatch submission.</summary>
    public void BindSunLevelScalarLut(int textureId)
    { SunLevelScalarLut = textureId; }

    /// <summary>Retains the surface lookup texture until dispatch.</summary>
    public void BindSurfaceLut(int textureId)
    { SurfaceLut = textureId; }

    /// <summary>Retains IrradianceAtlasImage for the next dispatch submission.</summary>
    public void BindIrradianceAtlasImage(GpuTexture irradianceAtlas, TextureAccess access = TextureAccess.ReadWrite)
    { IrradianceAtlas = new(irradianceAtlas, Access: access, Layered: true, Format: SizedInternalFormat.Rgba16f); }

    /// <summary>Retains DebugCounters for the next dispatch submission.</summary>
    public void BindDebugCounters(GpuAtomicCounterBuffer? debugCounters)
    { DebugCounters = debugCounters; }

    /// <summary>Stages AtlasLayout without uploading partial parameters.</summary>
    public void SetAtlasLayout(uint tileSizeTexels, uint tilesPerAxis, uint tilesPerAtlas, uint borderTexels)
    {
        RequireInputMutation();
        UboPacking.WriteUVec4(paramsBytes, AtlasLayoutOffsetBytes, tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels);
        StageParameters();
    }

    /// <summary>Stages RelightParams without uploading partial parameters.</summary>
    public void SetRelightParams(int frameIndex, uint texelsPerPagePerFrame, uint raysPerTexel, uint maxDdaSteps, bool debugCountersEnabled)
    {
        RequireInputMutation();
        UboPacking.WriteUVec4(
            paramsBytes,
            RelightUints0OffsetBytes,
            texelsPerPagePerFrame,
            raysPerTexel,
            maxDdaSteps,
            debugCountersEnabled ? 1u : 0u);

        int occResolution = ReadI32(RelightInts0OffsetBytes + 4);
        UboPacking.WriteIVec4(paramsBytes, RelightInts0OffsetBytes, frameIndex, occResolution, 0, 0);
        StageParameters();
    }

    /// <summary>Stages OccupancyMapping without uploading partial parameters.</summary>
    public void SetOccupancyMapping(int originMinCellX, int originMinCellY, int originMinCellZ, int ringX, int ringY, int ringZ, int resolution)
    {
        RequireInputMutation();
        UboPacking.WriteIVec4(paramsBytes, OccOriginMinCell0OffsetBytes, originMinCellX, originMinCellY, originMinCellZ, 0);
        UboPacking.WriteIVec4(paramsBytes, OccRing0OffsetBytes, ringX, ringY, ringZ, 0);

        int frameIndex = ReadI32(RelightInts0OffsetBytes);
        UboPacking.WriteIVec4(paramsBytes, RelightInts0OffsetBytes, frameIndex, resolution, 0, 0);
        StageParameters();
    }

    /// <summary>Supplies retained packed dispatch parameters.</summary>
    CpuUniformBuffer ILumonSceneRelightVoxelDdaComputeShaderBindings.Parameters => parameters;
    #endregion

    #region Private
    /// <summary>Adopts the executable and attaches the input mutation guard.</summary>
    private LumonSceneRelightVoxelDdaComputeShader(GpuComputePipeline pipeline) : base(pipeline)
    {
        parameters.SetWriteGuard(RequireInputMutation);
    }

    /// <summary>Packs retained values for one complete publication at dispatch.</summary>
    private void StageParameters()
    {
        parameters.SetBytes(paramsBytes);
    }

    /// <summary>Preserves the other components of a packed integer vector.</summary>
    private int ReadI32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(paramsBytes.AsSpan(offset, 4));
    #endregion
}
