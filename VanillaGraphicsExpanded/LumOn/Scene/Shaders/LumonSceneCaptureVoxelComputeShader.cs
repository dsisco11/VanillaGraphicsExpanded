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
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
[ShaderBindingSet(typeof(TraceGeometryBindingSet), Program = "Contract")]
internal sealed partial class LumonSceneCaptureVoxelComputeShader : IDisposable
{

    #region Private: GPU binding declarations
    /// <summary>Declares the VgeLumOnSceneCaptureVoxelParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnSceneCaptureVoxelParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    private partial GpuUniformBuffer Parameters { set; }
    /// <summary>Declares the vge_depthAtlas Image slot.</summary>
    [ShaderBinding("vge_depthAtlas", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    private partial GpuTextureBinding DepthAtlas { set; }
    /// <summary>Declares the vge_materialAtlas Image slot.</summary>
    [ShaderBinding("vge_materialAtlas", ShaderBindingKind.Image, 1, ShaderStageKind.Compute)]
    private partial GpuTextureBinding MaterialAtlas { set; }
    /// <summary>Declares the VgeCaptureWork StorageBlock slot.</summary>
    [ShaderBinding("VgeCaptureWork", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    private partial GpuShaderStorageBuffer CaptureWork { set; }
    /// <summary>Declares the VgePatchMetadata StorageBlock slot.</summary>
    [ShaderBinding("VgePatchMetadata", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    private partial GpuShaderStorageBuffer PatchMetadata { set; }
    /// <summary>Declares the VgeChunkSlotInfo StorageBlock slot.</summary>
    [ShaderBinding("VgeChunkSlotInfo", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    private partial GpuShaderStorageBuffer ChunkSlotInfo { set; }
    #endregion

    public static string ShaderName => Contract.Identity;

    private const int ParamsUboSizeBytes = 64; // uvec4 + ivec4 + ivec4 + ivec4

    private const int AtlasLayoutOffsetBytes = 0;




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

    /// <summary>Uploads parameters and assigns the generated uniform-buffer binding.</summary>
    private void ApplyParamsUbo()
    {
        paramsUbo ??= GpuUniformBuffer.Create(debugName: "LumOnScene.CaptureVoxel.ParamsUBO");
        paramsUbo.UploadOrResize(paramsBytes, ParamsUboSizeBytes, growExponentially: false);
        Parameters = paramsUbo;
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

    /// <summary>Assigns the typed GPU resource through its generated binding property.</summary>
    public void BindDepthAtlasImage(GpuTexture depthAtlas, TextureAccess access = TextureAccess.WriteOnly)
    {
        if (depthAtlas is null) throw new ArgumentNullException(nameof(depthAtlas));

        DepthAtlas = new(depthAtlas,
            Access: access,
            Level: 0,
            Layered: true,
            Layer: 0,
            Format: SizedInternalFormat.R16f);
    }

    /// <summary>Assigns the typed GPU resource through its generated binding property.</summary>
    public void BindMaterialAtlasImage(GpuTexture materialAtlas, TextureAccess access = TextureAccess.WriteOnly)
    {
        if (materialAtlas is null) throw new ArgumentNullException(nameof(materialAtlas));

        MaterialAtlas = new(materialAtlas,
            Access: access,
            Level: 0,
            Layered: true,
            Layer: 0,
            Format: SizedInternalFormat.Rgba8);
    }

    /// <summary>Assigns the typed GPU resource through its generated binding property.</summary>
    public void BindCaptureWorkSsbo(GpuShaderStorageBuffer captureWorkSsbo)
    {
        if (captureWorkSsbo is null) throw new ArgumentNullException(nameof(captureWorkSsbo));
        CaptureWork = captureWorkSsbo;
    }

    /// <summary>Assigns the typed GPU resource through its generated binding property.</summary>
    public void BindPatchMetaSsbo(GpuShaderStorageBuffer patchMetaSsbo)
    {
        if (patchMetaSsbo is null) throw new ArgumentNullException(nameof(patchMetaSsbo));
        PatchMetadata = patchMetaSsbo;
    }

    /// <summary>Assigns the typed GPU resource through its generated binding property.</summary>
    public void BindChunkSlotInfoSsbo(GpuShaderStorageBuffer chunkSlotInfoSsbo)
    {
        if (chunkSlotInfoSsbo is null) throw new ArgumentNullException(nameof(chunkSlotInfoSsbo));
        ChunkSlotInfo = chunkSlotInfoSsbo;
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
