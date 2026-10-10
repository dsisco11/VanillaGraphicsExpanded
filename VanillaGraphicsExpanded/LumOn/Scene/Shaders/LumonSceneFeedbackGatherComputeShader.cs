using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the compute shader contract and dispatch resources for this scene operation.</summary>
[ShaderProgram("Contract", "lumonscene_feedback_gather", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_feedback_gather.csh")]
internal sealed partial class LumonSceneFeedbackGatherComputeShader : GpuComputeProgram, ILumonSceneFeedbackGatherComputeShaderBindings
{

    public static string ShaderName => Contract.Identity;
    private const int ParamsUboSizeBytes = 32; // uvec4 + uvec4

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private readonly PackedUniformBuffer parameters = new(ParamsUboSizeBytes);

    private uint maxRequests;
    private uint sampleCount;
    private uint screenWidth;
    private uint screenHeight;

    private VgeFrameUniformBuffer? frameInputs;

    #region Public API
    /// <summary>Retains an explicit view snapshot for isolated dispatches.</summary>
    internal VgeFrameUniformBuffer FrameInputs { set { RequireInputMutation(); frameInputs = value; } }
    /// <summary>Uses the shared publication unless this dispatch owns an alternate view.</summary>
    CpuUniformBuffer ILumonSceneFeedbackGatherComputeShaderBindings.FrameInputs => frameInputs ?? VgeFrameRenderer.Current;
    /// <summary>Creates the executable before adopting its retained input owner.</summary>
    public static bool TryCreate(
        ICoreAPI api,
        out LumonSceneFeedbackGatherComputeShader? shader,
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
            layout: new LumonSceneComputeProgramLayouts.FeedbackGather()))
        {
            return false;
        }

        if (pipeline is null)
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + "[VGE] FeedbackGather pipeline creation returned null.";
            return false;
        }

        shader = new LumonSceneFeedbackGatherComputeShader(pipeline);
        return true;
    }

    /// <summary>Retains PatchIdGBuffer for the next dispatch submission.</summary>
    public void BindPatchIdGBuffer(int textureId)
    { PatchIdG = textureId; }

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

    /// <summary>Stages ScreenSize without uploading partial parameters.</summary>
    public void SetScreenSize(uint width, uint height)
    {
        RequireInputMutation();
        screenWidth = width;
        screenHeight = height;
        StageParameters();
    }

    public uint SampleCount
    {
        set
        {
            RequireInputMutation();
            sampleCount = value;
            StageParameters();
        }
    }

    /// <summary>Supplies retained packed dispatch parameters.</summary>
    CpuUniformBuffer ILumonSceneFeedbackGatherComputeShaderBindings.Parameters => parameters;
    #endregion

    #region Private
    /// <summary>Adopts the executable and attaches the input mutation guard.</summary>
    private LumonSceneFeedbackGatherComputeShader(GpuComputePipeline pipeline) : base(pipeline)
    {
        OwnUniformBuffer(parameters);
    }

    /// <summary>Packs retained values for one complete publication at dispatch.</summary>
    private void StageParameters()
    {
        UboPacking.WriteUVec4(paramsBytes, 0, maxRequests, 0u, sampleCount, 0u);
        UboPacking.WriteUVec4(paramsBytes, 16, screenWidth, screenHeight, 0u, 0u);
        parameters.SetBytes(paramsBytes);
    }
    #endregion
}
