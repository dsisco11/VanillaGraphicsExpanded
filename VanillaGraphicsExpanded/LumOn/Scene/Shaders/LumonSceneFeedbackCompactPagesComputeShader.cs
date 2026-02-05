using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal sealed class LumonSceneFeedbackCompactPagesComputeShader : IDisposable
{
    public const string ShaderName = "lumonscene_feedback_compact_pages";

    private const int PageUsageStampSamplerUnit = 0; // layout(binding=0)
    private const int PageTableMip0SamplerUnit = 1; // layout(binding=1)

    private const int PageRequestCountBindingIndex = 0; // layout(binding=0, offset=0)
    private const int PageRequestsSsboBindingIndex = 0; // layout(std430, binding=0)

    private const int MaxRequestsLocation = 0; // layout(location=0)
    private const int FrameStampLocation = 1; // layout(location=1)
    private const int ScanOffsetLocation = 2; // layout(location=2)
    private const int CompactModeLocation = 3; // layout(location=3)

    private readonly GpuComputePipeline pipeline;

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneFeedbackCompactPagesComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

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
            log: api.Logger))
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

    public IDisposable UseScope() => pipeline.UseScope();

    public void Use() => pipeline.Use();

    public void BindPageUsageStamp(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2DArray, unit: PageUsageStampSamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: PageUsageStampSamplerUnit);
    }

    public void BindPageTableMip0(int textureId)
    {
        GlStateCache.Current.BindTexture(TextureTarget.Texture2DArray, unit: PageTableMip0SamplerUnit, textureId: textureId);
        GpuSamplers.NearestClamp.Bind(unit: PageTableMip0SamplerUnit);
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

    public uint FrameStamp
    {
        set
        {
            Use();
            GL.Uniform1(FrameStampLocation, value);
        }
    }

    public uint ScanOffset
    {
        set
        {
            Use();
            GL.Uniform1(ScanOffsetLocation, value);
        }
    }

    public uint CompactMode
    {
        set
        {
            Use();
            GL.Uniform1(CompactModeLocation, value);
        }
    }

    public void DispatchBound(int numGroupsX, int numGroupsY, int numGroupsZ) => pipeline.DispatchBound(numGroupsX, numGroupsY, numGroupsZ);

    public void Dispose()
    {
        try
        {
            pipeline.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VGE] Exception disposing CompactPages compute shader: {ex}");
        }
    }
}
