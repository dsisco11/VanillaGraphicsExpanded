using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal sealed class LumonSceneRelightVoxelDdaComputeShader : IDisposable
{
    public const string ShaderName = "lumonscene_relight_voxel_dda";

    private const int DepthAtlasSamplerUnit = 0;
    private const int MaterialAtlasSamplerUnit = 1;
    private const int OccL0SamplerUnit = 2;
    private const int LightColorLutSamplerUnit = 3;
    private const int BlockLevelScalarLutSamplerUnit = 4;
    private const int SunLevelScalarLutSamplerUnit = 5;
    private const int MaterialPaletteSamplerUnit = 6;
    private const int SurfaceLutSamplerUnit = 7;

    private const int IrradianceAtlasImageUnit = 0; // layout(binding=0, rgba16f)

    private const int RelightWorkSsboBindingIndex = 0; // layout(std430, binding=0)
    private const int PatchMetaSsboBindingIndex = 1;   // layout(std430, binding=1)

    private const int DebugCountersBindingIndex = 0;   // layout(binding=0, offset=...)

    private const int TileSizeTexelsLocation = 0;
    private const int TilesPerAxisLocation = 1;
    private const int TilesPerAtlasLocation = 2;
    private const int BorderTexelsLocation = 3;

    private const int FrameIndexLocation = 4;
    private const int TexelsPerPagePerFrameLocation = 5;
    private const int RaysPerTexelLocation = 6;
    private const int MaxDdaStepsLocation = 7;

    private const int DebugCountersEnabledLocation = 8;

    private const int OccOriginMinCell0Location = 9; // ivec3
    private const int OccRing0Location = 10;         // ivec3
    private const int OccResolutionLocation = 11;    // int

    private readonly GpuComputePipeline pipeline;

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneRelightVoxelDdaComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

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
            log: api.Logger))
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

    public void BindOccL0(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture3D, unit: OccL0SamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: OccL0SamplerUnit);
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

    public void BindMaterialPalette(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit: MaterialPaletteSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: MaterialPaletteSamplerUnit);
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
        Use();
        GL.Uniform1(TileSizeTexelsLocation, tileSizeTexels);
        GL.Uniform1(TilesPerAxisLocation, tilesPerAxis);
        GL.Uniform1(TilesPerAtlasLocation, tilesPerAtlas);
        GL.Uniform1(BorderTexelsLocation, borderTexels);
    }

    public void SetRelightParams(int frameIndex, uint texelsPerPagePerFrame, uint raysPerTexel, uint maxDdaSteps, bool debugCountersEnabled)
    {
        Use();
        GL.Uniform1(FrameIndexLocation, frameIndex);
        GL.Uniform1(TexelsPerPagePerFrameLocation, texelsPerPagePerFrame);
        GL.Uniform1(RaysPerTexelLocation, raysPerTexel);
        GL.Uniform1(MaxDdaStepsLocation, maxDdaSteps);
        GL.Uniform1(DebugCountersEnabledLocation, debugCountersEnabled ? 1u : 0u);
    }

    public void SetOccupancyMapping(int originMinCellX, int originMinCellY, int originMinCellZ, int ringX, int ringY, int ringZ, int resolution)
    {
        Use();
        GL.Uniform3(OccOriginMinCell0Location, originMinCellX, originMinCellY, originMinCellZ);
        GL.Uniform3(OccRing0Location, ringX, ringY, ringZ);
        GL.Uniform1(OccResolutionLocation, resolution);
    }

    public void DispatchBound(int numGroupsX, int numGroupsY, int numGroupsZ) => pipeline.DispatchBound(numGroupsX, numGroupsY, numGroupsZ);

    public void Dispose()
    {
        try
        {
            pipeline.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VGE] Exception disposing RelightVoxelDda compute shader: {ex}");
        }
    }
}
