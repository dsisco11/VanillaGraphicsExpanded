using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
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
internal sealed partial class LumonSceneCaptureMeshCardComputeShader : GpuComputeProgram, ILumonSceneCaptureMeshCardComputeShaderBindings
{

    public static string ShaderName => Contract.Identity;

    private readonly LumonSceneCaptureMeshCardParameters parameters = new();


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

        if (!ComputeShaderAsset.TryCreateFromAssets(api: api,
            settings: new global::VanillaGraphicsExpanded.Rendering.Contracts.ShaderSettings(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Registry.FindProgram(ShaderName)),
            pipeline: out var pipeline,
            infoLog: out infoLog,
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
    public void BindDepthAtlasImage(GpuTexture depthAtlas, ImageAccess access = ImageAccess.WriteOnly)
    {
        if (depthAtlas is null) throw new ArgumentNullException(nameof(depthAtlas));

        DepthAtlas = new(depthAtlas,
            Access: access,
            Level: 0,
            Layered: true,
            Layer: 0,
            Format: TextureFormat.R16f);
    }

    /// <summary>Retains MaterialAtlasImage for the next dispatch submission.</summary>
    public void BindMaterialAtlasImage(GpuTexture materialAtlas, ImageAccess access = ImageAccess.WriteOnly)
    {
        if (materialAtlas is null) throw new ArgumentNullException(nameof(materialAtlas));

        MaterialAtlas = new(materialAtlas,
            Access: access,
            Level: 0,
            Layered: true,
            Layer: 0,
            Format: TextureFormat.Rgba8);
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
        parameters.SetAtlasLayout(tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels);
    }

    /// <summary>Stages the depth range used by mesh-card capture.</summary>
    public float CaptureDepthRange
    {
        set
        {
            RequireInputMutation();
            parameters.SetCaptureDepth(value);
        }
    }

    /// <summary>Supplies retained packed dispatch parameters.</summary>
    CpuUniformBuffer ILumonSceneCaptureMeshCardComputeShaderBindings.Parameters => parameters;
    #endregion

    #region Private
    /// <summary>Adopts the executable and attaches the input mutation guard.</summary>
    private LumonSceneCaptureMeshCardComputeShader(ComputeShaderAsset pipeline) : base(pipeline)
    {
        OwnUniformBuffer(parameters);
    }

    #endregion
}
