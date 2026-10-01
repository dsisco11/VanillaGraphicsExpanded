using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the compute shader contract and dispatch resources for this scene operation.</summary>
[ShaderProgram("Contract", "lumonscene_capture_meshcard", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_capture_meshcard.csh")]
internal sealed partial class LumonSceneCaptureMeshCardComputeShader : GpuComputeShader, ILumonSceneCaptureMeshCardComputeShaderBindings
{

    public static string ShaderName => Contract.Identity;

    private const int ParamsUboSizeBytes = 32; // uvec4 + vec4

    private const int AtlasLayoutOffsetBytes = 0;
    private const int CaptureFloats0OffsetBytes = 16;

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private readonly PackedUniformBuffer parameters = new(ParamsUboSizeBytes);

    private uint tileSizeTexels;
    private uint tilesPerAxis;
    private uint tilesPerAtlas;
    private uint borderTexels;
    private float captureDepthRange;

    #region Public API
    /// <summary>Creates the executable before adopting its retained input owner.</summary>
    public static bool TryCreate(
        ICoreAPI api,
        out LumonSceneCaptureMeshCardComputeShader? shader,
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
            layout: new LumonSceneComputeProgramLayouts.CaptureMeshCard()))
        {
            return false;
        }

        if (pipeline is null)
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + "[VGE] CaptureMeshCard pipeline creation returned null.";
            return false;
        }

        shader = new LumonSceneCaptureMeshCardComputeShader(pipeline);
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

    /// <summary>Retains MeshCardCaptureWorkSsbo for the next dispatch submission.</summary>
    public void BindMeshCardCaptureWorkSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        MeshCardCaptureWork = ssbo;
    }

    /// <summary>Retains PatchMetadataSsbo for the next dispatch submission.</summary>
    public void BindPatchMetadataSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        PatchMetadata = ssbo;
    }

    /// <summary>Retains TrianglesSsbo for the next dispatch submission.</summary>
    public void BindTrianglesSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        Triangles = ssbo;
    }

    /// <summary>Stages AtlasLayout without uploading partial parameters.</summary>
    public void SetAtlasLayout(uint tileSizeTexels, uint tilesPerAxis, uint tilesPerAtlas, uint borderTexels)
    {
        RequireInputMutation();
        this.tileSizeTexels = tileSizeTexels;
        this.tilesPerAxis = tilesPerAxis;
        this.tilesPerAtlas = tilesPerAtlas;
        this.borderTexels = borderTexels;
        StageParameters();
    }

    public float CaptureDepthRange
    {
        set
        {
            RequireInputMutation();
            captureDepthRange = value;
            StageParameters();
        }
    }

    /// <summary>Supplies retained packed dispatch parameters.</summary>
    CpuUniformBuffer ILumonSceneCaptureMeshCardComputeShaderBindings.Parameters => parameters;
    #endregion

    #region Private
    /// <summary>Adopts the executable and attaches the input mutation guard.</summary>
    private LumonSceneCaptureMeshCardComputeShader(GpuComputePipeline pipeline) : base(pipeline)
    {
        parameters.SetWriteGuard(RequireInputMutation);
    }

    /// <summary>Packs retained values for one complete publication at dispatch.</summary>
    private void StageParameters()
    {
        UboPacking.WriteUVec4(paramsBytes, AtlasLayoutOffsetBytes, tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels);
        UboPacking.WriteVec4(paramsBytes, CaptureFloats0OffsetBytes, captureDepthRange, 0f, 0f, 0f);
        parameters.SetBytes(paramsBytes);
    }
    #endregion
}
