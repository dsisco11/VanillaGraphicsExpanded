using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the compute shader contract and dispatch resources for this scene operation.</summary>
[ShaderProgram("Contract", "lumonscene_capture_voxel", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_capture_voxel.csh")]
internal sealed partial class LumonSceneCaptureVoxelComputeShader : IDisposable
{

    public static string ShaderName => Contract.Identity;

    private const int ParamsUboBinding = GpuBindingRegistry.Ubo.Object; // VGE_UBO_OBJECT_BINDING
    private const int ParamsUboSizeBytes = 64; // uvec4 + ivec4 + ivec4 + ivec4

    private const int AtlasLayoutOffsetBytes = 0;

    private const int DepthAtlasImageUnit = 0;    // layout(binding=0, r16f)
    private const int MaterialAtlasImageUnit = 1; // layout(binding=1, rgba8)


    private const int CaptureWorkSsboBindingIndex = 0; // layout(std430, binding=0)
    private const int PatchMetaSsboBindingIndex = 1;   // layout(std430, binding=1)
    private const int ChunkSlotInfoSsboBindingIndex = 2; // layout(std430, binding=2)

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private GpuUniformBuffer? paramsUbo;

    private readonly GpuComputePipeline pipeline;
    private readonly Geometry.TraceGeometryComputeBindings sharedGeometry = new();
    public SurfaceWorkDiagnostics Diagnostics { get; } = new();

    #region Measured dispatch
    /// <summary>Dispatches capture with bounded asynchronous outcome and timing measurement.</summary>
    public void Dispatch(int groupsX, int groupsY, int pages)
    {
        bool measured = Diagnostics.Begin(SurfaceWorkStage.Capture, pages);
        try
        {
            UboPacking.WriteUVec4(paramsBytes, 16, measured ? 1u : 0u, 0, 0, 0);
            ApplyParamsUbo();
            GL.DispatchCompute(groupsX, groupsY, pages);
        }
        finally { Diagnostics.End(); }
    }
    #endregion

    /// <summary>Binds the shared geometry and independent logical domains.</summary>
    public void BindSharedGeometry(Geometry.TraceGeometryGpuScene? scene) => sharedGeometry.Bind(scene);

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

    public void SetAtlasLayout(uint tileSizeTexels, uint tilesPerAxis, uint tilesPerAtlas, uint borderTexels)
    {
        UboPacking.WriteUVec4(paramsBytes, AtlasLayoutOffsetBytes, tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels);
        ApplyParamsUbo();
    }

    public void Dispose()
    {
        try
        {
            Diagnostics.Dispose();
            sharedGeometry.Dispose();
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
