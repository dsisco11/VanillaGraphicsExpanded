using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
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

    private readonly LumonSceneRelightVoxelDdaParameters parameters = new();

    private VgeFrameUniformBuffer? frameInputs;

    #region Public API
    /// <summary>Retains an explicit view snapshot for isolated dispatches.</summary>
    internal VgeFrameUniformBuffer FrameInputs { set { RequireInputMutation(); frameInputs = value; } }
    /// <summary>Uses the shared publication unless this dispatch owns an alternate view.</summary>
    CpuUniformBuffer ILumonSceneRelightVoxelDdaComputeShaderBindings.FrameInputs => frameInputs ?? VgeFrameRenderer.Current;
    /// <summary>Creates the executable before adopting its retained input owner.</summary>
    public static bool TryCreate(
        ICoreAPI api,
        out LumonSceneRelightVoxelDdaComputeShader? shader,
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


    /// <summary>Retains RelightWorkSsbo for the next dispatch submission.</summary>
    public void BindRelightWorkSsbo(GpuShaderStorageBuffer ssbo)
    { RelightWork = ssbo; }

    /// <summary>Retains PatchMetaSsbo for the next dispatch submission.</summary>
    public void BindPatchMetaSsbo(GpuShaderStorageBuffer ssbo)
    { PatchMetadata = ssbo; }

    /// <summary>Retains IrradianceAtlasImage for the next dispatch submission.</summary>
    public void BindIrradianceAtlasImage(GpuTexture irradianceAtlas, ImageAccess access = ImageAccess.ReadWrite)
    { IrradianceAtlas = new(irradianceAtlas, Access: access, Layered: true, Format: TextureFormat.Rgba16f); }

    /// <summary>Retains DebugCounters for the next dispatch submission.</summary>
    public void BindDebugCounters(GpuAtomicCounterBuffer? debugCounters)
    { DebugCounters = debugCounters; }

    /// <summary>Stages AtlasLayout without uploading partial parameters.</summary>
    public void SetAtlasLayout(uint tileSizeTexels, uint tilesPerAxis, uint tilesPerAtlas, uint borderTexels)
    {
        RequireInputMutation();
        parameters.SetAtlasLayout(tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels);
    }

    /// <summary>Stages relighting budgets independently of the shared rendering frame.</summary>
    public void SetRelightParams(uint texelsPerPagePerFrame, uint raysPerTexel, uint maxDdaSteps, bool debugCountersEnabled)
    {
        RequireInputMutation();
        parameters.SetRelight(texelsPerPagePerFrame, raysPerTexel, maxDdaSteps, debugCountersEnabled);
    }

    /// <summary>Stages OccupancyMapping without uploading partial parameters.</summary>
    public void SetOccupancyMapping(int originMinCellX, int originMinCellY, int originMinCellZ, int ringX, int ringY, int ringZ, int resolution)
    {
        RequireInputMutation();
        parameters.SetOccupancy(originMinCellX, originMinCellY, originMinCellZ, ringX, ringY, ringZ, resolution);
    }

    /// <summary>Supplies retained packed dispatch parameters.</summary>
    CpuUniformBuffer ILumonSceneRelightVoxelDdaComputeShaderBindings.Parameters => parameters;
    #endregion

    #region Private
    /// <summary>Adopts the executable and attaches the input mutation guard.</summary>
    private LumonSceneRelightVoxelDdaComputeShader(ComputeShaderAsset pipeline) : base(pipeline)
    {
        OwnUniformBuffer(parameters);
    }


    #endregion
}
