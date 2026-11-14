using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
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


    private readonly LumonSceneFeedbackGatherParameters parameters = new();


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

        if (!ComputeShaderAsset.TryCreateFromAssets(api: api,
            settings: new global::VanillaGraphicsExpanded.Rendering.Contracts.ShaderSettings(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Registry.FindProgram(ShaderName)),
            pipeline: out var pipeline,
            infoLog: out infoLog,
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


    /// <summary>Retains RequestCounter for the next dispatch submission.</summary>
    public void BindRequestCounter(GpuAtomicCounterBuffer counter)
    { RequestCounter = counter; }

    /// <summary>Retains RequestsSsbo for the next dispatch submission.</summary>
    public void BindRequestsSsbo(GpuShaderStorageBuffer ssbo)
    { PageRequests = ssbo; }

    /// <summary>Stages MaxRequests in the retained dispatch inputs.</summary>
    public uint MaxRequests
    {
        set
        {
            RequireInputMutation();
            parameters.SetMaxRequests(value);
        }
    }

    /// <summary>Stages ScreenSize without uploading partial parameters.</summary>
    public void SetScreenSize(uint width, uint height)
    {
        RequireInputMutation();
        parameters.SetScreenSize(width, height);
    }

    /// <summary>Stages SampleCount in the retained dispatch inputs.</summary>
    public uint SampleCount
    {
        set
        {
            RequireInputMutation();
            parameters.SetSampleCount(value);
        }
    }

    /// <summary>Supplies retained packed dispatch parameters.</summary>
    CpuUniformBuffer ILumonSceneFeedbackGatherComputeShaderBindings.Parameters => parameters;
    #endregion

    #region Private
    /// <summary>Adopts the executable and attaches the input mutation guard.</summary>
    private LumonSceneFeedbackGatherComputeShader(ComputeShaderAsset pipeline) : base(pipeline)
    {
        OwnUniformBuffer(parameters);
    }

    #endregion
}
