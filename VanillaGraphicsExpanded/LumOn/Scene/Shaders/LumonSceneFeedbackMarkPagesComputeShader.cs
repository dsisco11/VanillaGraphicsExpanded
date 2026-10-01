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
internal sealed partial class LumonSceneFeedbackMarkPagesComputeShader : GpuComputeShader, ILumonSceneFeedbackMarkPagesComputeShaderBindings
{

    public static string ShaderName => Contract.Identity;
    private const int ParamsUboSizeBytes = 16; // uvec4

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private readonly PackedUniformBuffer parameters = new(ParamsUboSizeBytes);

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

    public uint FrameStamp
    {
        set
        {
            RequireInputMutation();
            UboPacking.WriteUVec4(paramsBytes, 0, value, 0u, 0u, 0u);
            StageParameters();
        }
    }

    /// <summary>Retains DebugCounters for the next dispatch submission.</summary>
    public void BindDebugCounters(GpuAtomicCounterBuffer? debugCounters)
    { DebugCounters = debugCounters; }

    /// <summary>Retains PatchIdGBuffer for the next dispatch submission.</summary>
    public void BindPatchIdGBuffer(int textureId)
    { PatchIdG = textureId; }

    /// <summary>Retains ChunkSlotGenerationTex for the next dispatch submission.</summary>
    public void BindChunkSlotGenerationTex(int textureId)
    { ChunkSlotGenerationTex = textureId; }

    /// <summary>Retains PageUsageStampImage for the next dispatch submission.</summary>
    public void BindPageUsageStampImage(GpuTexture texture, TextureAccess access = TextureAccess.ReadWrite)
    { PageUsageStamp = new GpuTextureBinding(texture, Access: access, Layered: true, Format: SizedInternalFormat.R32ui); }

    /// <summary>Supplies retained packed dispatch parameters.</summary>
    CpuUniformBuffer ILumonSceneFeedbackMarkPagesComputeShaderBindings.Parameters => parameters;
    #endregion

    #region Private
    /// <summary>Adopts the executable and attaches the input mutation guard.</summary>
    private LumonSceneFeedbackMarkPagesComputeShader(GpuComputePipeline pipeline) : base(pipeline)
    {
        parameters.SetWriteGuard(RequireInputMutation);
    }

    /// <summary>Packs retained values for one complete publication at dispatch.</summary>
    private void StageParameters()
    {
        parameters.SetBytes(paramsBytes);
    }
    #endregion
}
