using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal sealed class LumonSceneFeedbackGatherComputeShader : IDisposable
{
    public const string ShaderName = "lumonscene_feedback_gather";

    private const int PatchIdGBufferSamplerUnit = 0; // layout(binding=0)

    private const int PageRequestCountBindingIndex = 0; // layout(binding=0, offset=0)
    private const int PageRequestsSsboBindingIndex = 0; // layout(std430, binding=0)

    private const int MaxRequestsLocation = 0; // layout(location=0)
    private const int FrameIndexLocation = 1;  // layout(location=1)
    private const int ScreenSizeLocation = 2;  // layout(location=2) uvec2
    private const int SampleCountLocation = 3; // layout(location=3)

    private readonly GpuComputePipeline pipeline;

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneFeedbackGatherComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

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
            log: api.Logger))
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

    public IDisposable UseScope() => pipeline.UseScope();

    public void Use() => pipeline.Use();

    public void BindPatchIdGBuffer(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit: PatchIdGBufferSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: PatchIdGBufferSamplerUnit);
    }

    public void BindRequestCounter(GpuAtomicCounterBuffer counter)
    {
        if (counter is null) throw new ArgumentNullException(nameof(counter));
        counter.BindBase(PageRequestCountBindingIndex);
    }

    public void BindRequestsSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        ssbo.BindBase(PageRequestsSsboBindingIndex);
    }

    public uint MaxRequests
    {
        set
        {
            Use();
            GL.Uniform1(MaxRequestsLocation, value);
        }
    }

    public uint FrameIndex
    {
        set
        {
            Use();
            GL.Uniform1(FrameIndexLocation, value);
        }
    }

    public void SetScreenSize(uint width, uint height)
    {
        Use();
        GL.Uniform2(ScreenSizeLocation, width, height);
    }

    public uint SampleCount
    {
        set
        {
            Use();
            GL.Uniform1(SampleCountLocation, value);
        }
    }

    public void Dispose()
    {
        try
        {
            pipeline.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VGE] Exception disposing FeedbackGather compute shader: {ex}");
        }
    }
}
