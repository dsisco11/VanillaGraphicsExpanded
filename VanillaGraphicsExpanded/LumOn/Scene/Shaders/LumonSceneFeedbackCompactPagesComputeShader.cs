using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the compute shader contract and dispatch resources for this scene operation.</summary>
[ShaderProgram("Contract", "lumonscene_feedback_compact_pages", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_feedback_compact_pages.csh")]
internal sealed partial class LumonSceneFeedbackCompactPagesComputeShader : GpuComputeProgram, ILumonSceneFeedbackCompactPagesComputeShaderBindings
{

    public static string ShaderName => Contract.Identity;
    private const int ParamsUboSizeBytes = 16; // uvec4

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private readonly PackedUniformBuffer parameters = new(ParamsUboSizeBytes);

    private uint maxRequests;
    private uint frameStamp;
    private uint scanOffset;
    private uint compactMode;

    #region Public API
    /// <summary>Creates the executable before adopting its retained input owner.</summary>
    public static bool TryCreate(
        ICoreAPI api,
        out LumonSceneFeedbackCompactPagesComputeShader? shader,
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
            layout: new LumonSceneComputeProgramLayouts.FeedbackCompactPages()))
        {
            return false;
        }

        if (pipeline is null)
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + "[VGE] CompactPages pipeline creation returned null.";
            return false;
        }

        shader = new LumonSceneFeedbackCompactPagesComputeShader(pipeline);
        return true;
    }

    /// <summary>Retains PageUsageStamp for the next dispatch submission.</summary>
    public void BindPageUsageStamp(int textureId)
    { PageUsageStamp = textureId; }

    /// <summary>Retains PageTableMip0 for the next dispatch submission.</summary>
    public void BindPageTableMip0(int textureId)
    { PageTableMip0 = textureId; }

    /// <summary>Retains RequestCounter for the next dispatch submission.</summary>
    public void BindRequestCounter(GpuAtomicCounterBuffer counter)
    { RequestCounter = counter; }

    /// <summary>Retains RequestsSsbo for the next dispatch submission.</summary>
    public void BindRequestsSsbo(GpuShaderStorageBuffer ssbo)
    { PageRequests = ssbo; }

    public uint MaxRequests
    {
        set
        {
            RequireInputMutation();
            maxRequests = value;
            StageParameters();
        }
    }

    public uint FrameStamp
    {
        set
        {
            RequireInputMutation();
            frameStamp = value;
            StageParameters();
        }
    }

    public uint ScanOffset
    {
        set
        {
            RequireInputMutation();
            scanOffset = value;
            StageParameters();
        }
    }

    public uint CompactMode
    {
        set
        {
            RequireInputMutation();
            compactMode = value;
            StageParameters();
        }
    }

    /// <summary>Supplies retained packed dispatch parameters.</summary>
    CpuUniformBuffer ILumonSceneFeedbackCompactPagesComputeShaderBindings.Parameters => parameters;
    #endregion

    #region Private
    /// <summary>Adopts the executable and attaches the input mutation guard.</summary>
    private LumonSceneFeedbackCompactPagesComputeShader(GpuComputePipeline pipeline) : base(pipeline)
    {
        OwnUniformBuffer(parameters);
    }

    /// <summary>Packs retained values for one complete publication at dispatch.</summary>
    private void StageParameters()
    {
        UboPacking.WriteUVec4(paramsBytes, 0, maxRequests, frameStamp, scanOffset, compactMode);
        parameters.SetBytes(paramsBytes);
    }
    #endregion
}
