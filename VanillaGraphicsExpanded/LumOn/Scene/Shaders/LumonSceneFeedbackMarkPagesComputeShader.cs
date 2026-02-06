using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal sealed class LumonSceneFeedbackMarkPagesComputeShader : IDisposable
{
    public const string ShaderName = "lumonscene_feedback_mark_pages";

    private const int ParamsUboBinding = GpuBindingRegistry.Ubo.Object; // VGE_UBO_OBJECT_BINDING
    private const int ParamsUboSizeBytes = 16; // uvec4

    private const int PatchIdGBufferSamplerUnit = 0; // layout(binding=0)
    private const int ChunkSlotGenerationSamplerUnit = 1; // layout(binding=1)

    private const int PageUsageStampImageUnit = 0; // layout(binding=0, r32ui)

    private const int MarkDebugCountersBindingIndex = 0; // layout(binding=0, offset=...)

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private GpuUniformBuffer? paramsUbo;

    private readonly GpuComputePipeline pipeline;

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneFeedbackMarkPagesComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

        paramsUbo = GpuUniformBuffer.Create(debugName: "LumOnScene.MarkPages.ParamsUBO");
    }

    private void ApplyParamsUbo()
    {
        paramsUbo ??= GpuUniformBuffer.Create(debugName: "LumOnScene.MarkPages.ParamsUBO");
        paramsUbo.UploadOrResize(paramsBytes, ParamsUboSizeBytes, growExponentially: false);
        paramsUbo.BindBase(ParamsUboBinding);
    }

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

    public IDisposable UseScope() => pipeline.UseScope();

    public void Use() => pipeline.Use();

    public uint FrameStamp
    {
        set
        {
            UboPacking.WriteUVec4(paramsBytes, 0, value, 0u, 0u, 0u);
            ApplyParamsUbo();
        }
    }

    public void BindDebugCounters(GpuAtomicCounterBuffer? debugCounters)
    {
        debugCounters?.BindBase(MarkDebugCountersBindingIndex);
    }

    public void BindPatchIdGBuffer(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit: PatchIdGBufferSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: PatchIdGBufferSamplerUnit);
    }

    public void BindChunkSlotGenerationTex(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit: ChunkSlotGenerationSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: ChunkSlotGenerationSamplerUnit);
    }

    public void BindPageUsageStampImage(GpuTexture texture, TextureAccess access = TextureAccess.ReadWrite)
    {
        if (texture is null) throw new ArgumentNullException(nameof(texture));

        texture.BindImageUnit(
            unit: PageUsageStampImageUnit,
            access: access,
            level: 0,
            layered: true,
            layer: 0,
            format: SizedInternalFormat.R32ui);
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
            Debug.WriteLine($"[VGE] Exception disposing MarkPages compute shader: {ex}");
        }
    }
}
