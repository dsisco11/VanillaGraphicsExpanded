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
internal sealed partial class LumonSceneCaptureVoxelComputeShader : TraceGeometryComputeShader, ILumonSceneCaptureVoxelComputeShaderBindings
{

    public static string ShaderName => Contract.Identity;

    private const int ParamsUboSizeBytes = 64; // uvec4 + ivec4 + ivec4 + ivec4

    private const int AtlasLayoutOffsetBytes = 0;

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private readonly PackedUniformBuffer parameters = new(ParamsUboSizeBytes);

    public SurfaceWorkDiagnostics Diagnostics { get; } = new();

    #region Public API
    /// <summary>Dispatches capture with bounded asynchronous outcome and timing measurement.</summary>
    public override void Dispatch(int groupsX, int groupsY = 1, int pages = 1)
    {
        RequireInputMutation();
        bool measured = Diagnostics.Begin(SurfaceWorkStage.Capture, pages);
        try
        {
            UboPacking.WriteUVec4(paramsBytes, 16, measured ? 1u : 0u, 0, 0, 0);
            StageParameters();
            base.Dispatch(groupsX, groupsY, pages);
        }
        finally { Diagnostics.End(); }
    }

    /// <summary>Creates the executable before adopting its retained input owner.</summary>
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

    /// <summary>Retains DepthAtlasImage for the next dispatch submission.</summary>
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

    /// <summary>Retains MaterialAtlasImage for the next dispatch submission.</summary>
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

    /// <summary>Retains CaptureWorkSsbo for the next dispatch submission.</summary>
    public void BindCaptureWorkSsbo(GpuShaderStorageBuffer captureWorkSsbo)
    {
        if (captureWorkSsbo is null) throw new ArgumentNullException(nameof(captureWorkSsbo));
        CaptureWork = captureWorkSsbo;
    }

    /// <summary>Retains PatchMetaSsbo for the next dispatch submission.</summary>
    public void BindPatchMetaSsbo(GpuShaderStorageBuffer patchMetaSsbo)
    {
        if (patchMetaSsbo is null) throw new ArgumentNullException(nameof(patchMetaSsbo));
        PatchMetadata = patchMetaSsbo;
    }

    /// <summary>Retains ChunkSlotInfoSsbo for the next dispatch submission.</summary>
    public void BindChunkSlotInfoSsbo(GpuShaderStorageBuffer chunkSlotInfoSsbo)
    {
        if (chunkSlotInfoSsbo is null) throw new ArgumentNullException(nameof(chunkSlotInfoSsbo));
        ChunkSlotInfo = chunkSlotInfoSsbo;
    }

    /// <summary>Stages AtlasLayout without uploading partial parameters.</summary>
    public void SetAtlasLayout(uint tileSizeTexels, uint tilesPerAxis, uint tilesPerAtlas, uint borderTexels)
    {
        RequireInputMutation();
        UboPacking.WriteUVec4(paramsBytes, AtlasLayoutOffsetBytes, tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels);
        StageParameters();
    }

    /// <summary>Supplies retained packed dispatch parameters.</summary>
    CpuUniformBuffer ILumonSceneCaptureVoxelComputeShaderBindings.Parameters => parameters;
    /// <summary>Supplies counter storage admitted by the measured dispatch.</summary>
    GpuShaderStorageBuffer? ILumonSceneCaptureVoxelComputeShaderBindings.DiagnosticCounters => Diagnostics.ActiveBuffer;
    #endregion

    #region Protected API
    /// <summary>Releases diagnostic storage independently of native executable retirement.</summary>
    protected override void ReleaseResources()
    {
        Diagnostics.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Adopts the executable and attaches the input mutation guard.</summary>
    private LumonSceneCaptureVoxelComputeShader(GpuComputePipeline pipeline) : base(pipeline)
    {
        OwnUniformBuffer(parameters);
    }

    /// <summary>Packs retained values for one complete publication at dispatch.</summary>
    private void StageParameters()
    {
        parameters.SetBytes(paramsBytes);
    }
    #endregion
}
