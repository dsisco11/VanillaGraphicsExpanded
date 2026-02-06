using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal sealed class LumonSceneCaptureVoxelComputeShader : IDisposable
{
    public const string ShaderName = "lumonscene_capture_voxel";

    private const int ParamsUboBinding = GpuBindingRegistry.Ubo.Object; // VGE_UBO_OBJECT_BINDING
    private const int ParamsUboSizeBytes = 64; // uvec4 + ivec4 + ivec4 + ivec4

    private const int AtlasLayoutOffsetBytes = 0;
    private const int OccOriginMinCell0OffsetBytes = 16;
    private const int OccRing0OffsetBytes = 32;
    private const int OccInts0OffsetBytes = 48;

    private const int DepthAtlasImageUnit = 0;    // layout(binding=0, r16f)
    private const int MaterialAtlasImageUnit = 1; // layout(binding=1, rgba8)

    private const int OccL0SamplerUnit = 2;            // layout(binding=2)
    private const int MaterialPaletteSamplerUnit = 3;  // layout(binding=3)

    private const int CaptureWorkSsboBindingIndex = 0; // layout(std430, binding=0)
    private const int PatchMetaSsboBindingIndex = 1;   // layout(std430, binding=1)
    private const int ChunkSlotInfoSsboBindingIndex = 2; // layout(std430, binding=2)

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private GpuUniformBuffer? paramsUbo;

    private readonly GpuComputePipeline pipeline;

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneCaptureVoxelComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

        paramsUbo = GpuUniformBuffer.Create(debugName: "LumOnScene.CaptureVoxel.ParamsUBO");
    }

    private void ApplyParamsUbo()
    {
        paramsUbo ??= GpuUniformBuffer.Create(debugName: "LumOnScene.CaptureVoxel.ParamsUBO");
        paramsUbo.UploadOrResize(paramsBytes, ParamsUboSizeBytes, growExponentially: false);
        paramsUbo.BindBase(ParamsUboBinding);
    }

    public static bool TryCreate(
        ICoreAPI api,
        out LumonSceneCaptureVoxelComputeShader? shader,
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
            layout: new LumonSceneComputeProgramLayouts.CaptureVoxel()))
        {
            return false;
        }

        if (pipeline is null)
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + "[VGE] CaptureVoxel pipeline creation returned null.";
            return false;
        }

        shader = new LumonSceneCaptureVoxelComputeShader(pipeline);
        return true;
    }

    public IDisposable UseScope() => pipeline.UseScope();

    public void Use() => pipeline.Use();

    public void BindDepthAtlasImage(GpuTexture depthAtlas, TextureAccess access = TextureAccess.WriteOnly)
    {
        if (depthAtlas is null) throw new ArgumentNullException(nameof(depthAtlas));

        depthAtlas.BindImageUnit(
            unit: DepthAtlasImageUnit,
            access: access,
            level: 0,
            layered: true,
            layer: 0,
            format: SizedInternalFormat.R16f);
    }

    public void BindMaterialAtlasImage(GpuTexture materialAtlas, TextureAccess access = TextureAccess.WriteOnly)
    {
        if (materialAtlas is null) throw new ArgumentNullException(nameof(materialAtlas));

        materialAtlas.BindImageUnit(
            unit: MaterialAtlasImageUnit,
            access: access,
            level: 0,
            layered: true,
            layer: 0,
            format: SizedInternalFormat.Rgba8);
    }

    public void BindOccL0(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture3D, unit: OccL0SamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: OccL0SamplerUnit);
    }

    public void BindMaterialPalette(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit: MaterialPaletteSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: MaterialPaletteSamplerUnit);
    }

    public void BindCaptureWorkSsbo(GpuShaderStorageBuffer captureWorkSsbo)
    {
        if (captureWorkSsbo is null) throw new ArgumentNullException(nameof(captureWorkSsbo));
        captureWorkSsbo.BindBase(CaptureWorkSsboBindingIndex);
    }

    public void BindPatchMetaSsbo(GpuShaderStorageBuffer patchMetaSsbo)
    {
        if (patchMetaSsbo is null) throw new ArgumentNullException(nameof(patchMetaSsbo));
        patchMetaSsbo.BindBase(PatchMetaSsboBindingIndex);
    }

    public void BindChunkSlotInfoSsbo(GpuShaderStorageBuffer chunkSlotInfoSsbo)
    {
        if (chunkSlotInfoSsbo is null) throw new ArgumentNullException(nameof(chunkSlotInfoSsbo));
        chunkSlotInfoSsbo.BindBase(ChunkSlotInfoSsboBindingIndex);
    }

    public void SetOccupancyMapping(int originMinCellX, int originMinCellY, int originMinCellZ, int ringX, int ringY, int ringZ, int resolution)
    {
        UboPacking.WriteIVec4(paramsBytes, OccOriginMinCell0OffsetBytes, originMinCellX, originMinCellY, originMinCellZ, 0);
        UboPacking.WriteIVec4(paramsBytes, OccRing0OffsetBytes, ringX, ringY, ringZ, 0);
        UboPacking.WriteIVec4(paramsBytes, OccInts0OffsetBytes, resolution, 0, 0, 0);
        ApplyParamsUbo();
    }

    public void SetAtlasLayout(uint tileSizeTexels, uint tilesPerAxis, uint tilesPerAtlas, uint borderTexels)
    {
        UboPacking.WriteUVec4(paramsBytes, AtlasLayoutOffsetBytes, tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels);
        ApplyParamsUbo();
    }

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
            Debug.WriteLine($"[VGE] Exception disposing CaptureVoxel compute shader: {ex}");
        }
    }
}
