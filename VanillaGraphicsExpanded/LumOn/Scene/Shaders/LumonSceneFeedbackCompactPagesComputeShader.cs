using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
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


    private readonly LumonSceneFeedbackCompactPagesParameters parameters = new();


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

        if (!ComputeShaderAsset.TryCreateFromAssets(api: api,
            settings: new global::VanillaGraphicsExpanded.Rendering.Contracts.ShaderSettings(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Registry.FindProgram(ShaderName)),
            pipeline: out var pipeline,
            infoLog: out infoLog,
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

    /// <summary>Stages FrameStamp in the retained dispatch inputs.</summary>
    public uint FrameStamp
    {
        set
        {
            RequireInputMutation();
            parameters.SetFrameStamp(value);
        }
    }

    /// <summary>Stages ScanOffset in the retained dispatch inputs.</summary>
    public uint ScanOffset
    {
        set
        {
            RequireInputMutation();
            parameters.SetScanOffset(value);
        }
    }

    /// <summary>Stages CompactMode in the retained dispatch inputs.</summary>
    public uint CompactMode
    {
        set
        {
            RequireInputMutation();
            parameters.SetCompactMode(value);
        }
    }

    /// <summary>Supplies retained packed dispatch parameters.</summary>
    CpuUniformBuffer ILumonSceneFeedbackCompactPagesComputeShaderBindings.Parameters => parameters;
    #endregion

    #region Private
    /// <summary>Adopts the executable and attaches the input mutation guard.</summary>
    private LumonSceneFeedbackCompactPagesComputeShader(ComputeShaderAsset pipeline) : base(pipeline)
    {
        OwnUniformBuffer(parameters);
    }

    #endregion
}
