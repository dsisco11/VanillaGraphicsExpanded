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
[ShaderProgram("Contract", "lumonscene_feedback_mark_pages", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_feedback_mark_pages.csh")]
internal sealed partial class LumonSceneFeedbackMarkPagesComputeShader : GpuComputeProgram, ILumonSceneFeedbackMarkPagesComputeShaderBindings
{

    public static string ShaderName => Contract.Identity;


    private readonly LumonSceneFeedbackMarkPagesParameters parameters = new();

    #region Public API
    /// <summary>Creates the executable before adopting its retained input owner.</summary>
    public static bool TryCreate(
        ICoreAPI api,
        out LumonSceneFeedbackMarkPagesComputeShader? shader,
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
            layout: new LumonSceneComputeProgramLayouts.FeedbackMarkPages()))
        {
            return false;
        }

        if (pipeline is null)
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + "[VGE] MarkPages pipeline creation returned null.";
            return false;
        }

        shader = new LumonSceneFeedbackMarkPagesComputeShader(pipeline);
        return true;
    }

    /// <summary>Stages the publication stamp used to mark requested pages.</summary>
    public uint FrameStamp
    {
        set
        {
            RequireInputMutation();
            parameters.SetFrame(value);
        }
    }

    /// <summary>Retains DebugCounters for the next dispatch submission.</summary>
    public void BindDebugCounters(GpuAtomicCounterBuffer? debugCounters)
    { DebugCounters = debugCounters; }

    /// <summary>Retains PageUsageStampImage for the next dispatch submission.</summary>
    public void BindPageUsageStampImage(GpuTexture texture, ImageAccess access = ImageAccess.ReadWrite)
    { PageUsageStamp = new RHIImageBinding(texture, Access: access, Layered: true, Format: TextureFormat.R32ui); }

    /// <summary>Supplies retained packed dispatch parameters.</summary>
    CpuUniformBuffer ILumonSceneFeedbackMarkPagesComputeShaderBindings.Parameters => parameters;
    #endregion

    #region Private
    /// <summary>Adopts the executable and attaches the input mutation guard.</summary>
    private LumonSceneFeedbackMarkPagesComputeShader(ComputeShaderAsset pipeline) : base(pipeline)
    {
        OwnUniformBuffer(parameters);
    }

    #endregion
}
