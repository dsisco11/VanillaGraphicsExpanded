using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal sealed class LumonSceneFeedbackGatherComputeShader : IDisposable
{
    public const string ShaderName = "lumonscene_feedback_gather";

    private const int ParamsUboBinding = GpuBindingRegistry.Ubo.Object; // VGE_UBO_OBJECT_BINDING
    private const int ParamsUboSizeBytes = 32; // uvec4 + uvec4

    private const int PatchIdGBufferSamplerUnit = 0; // layout(binding=0)

    private const int PageRequestCountBindingIndex = 0; // layout(binding=0, offset=0)
    private const int PageRequestsSsboBindingIndex = 0; // layout(std430, binding=0)

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private GpuUniformBuffer? paramsUbo;

    private uint maxRequests;
    private uint frameIndex;
    private uint sampleCount;
    private uint screenWidth;
    private uint screenHeight;

    private readonly GpuComputePipeline pipeline;

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneFeedbackGatherComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

        paramsUbo = GpuUniformBuffer.Create(debugName: "LumOnScene.FeedbackGather.ParamsUBO");
    }

    private void ApplyParamsUbo()
    {
        paramsUbo ??= GpuUniformBuffer.Create(debugName: "LumOnScene.FeedbackGather.ParamsUBO");
        UboPacking.WriteUVec4(paramsBytes, 0, maxRequests, frameIndex, sampleCount, 0u);
        UboPacking.WriteUVec4(paramsBytes, 16, screenWidth, screenHeight, 0u, 0u);
        paramsUbo.UploadOrResize(paramsBytes, ParamsUboSizeBytes, growExponentially: false);
        paramsUbo.BindBase(ParamsUboBinding);
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
            maxRequests = value;
            ApplyParamsUbo();
        }
    }

    public uint FrameIndex
    {
        set
        {
            frameIndex = value;
            ApplyParamsUbo();
        }
    }

    public void SetScreenSize(uint width, uint height)
    {
        screenWidth = width;
        screenHeight = height;
        ApplyParamsUbo();
    }

    public uint SampleCount
    {
        set
        {
            sampleCount = value;
            ApplyParamsUbo();
        }
    }

    public void Dispose()
    {
        try
        {
            paramsUbo?.Dispose();
            paramsUbo = null;
            pipeline.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VGE] Exception disposing FeedbackGather compute shader: {ex}");
        }
    }
}
