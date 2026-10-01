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
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
internal sealed partial class LumonSceneFeedbackCompactPagesComputeShader : IDisposable
{

    #region Private: GPU binding declarations
    /// <summary>Declares the VgeLumOnSceneFeedbackCompactParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnSceneFeedbackCompactParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    private partial GpuUniformBuffer Parameters { set; }
    /// <summary>Declares the vge_pageUsageStamp Sampler slot.</summary>
    [ShaderBinding("vge_pageUsageStamp", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute)]
    private partial GpuTexture PageUsageStamp { set; }
    /// <summary>Declares the vge_pageTableMip0 Sampler slot.</summary>
    [ShaderBinding("vge_pageTableMip0", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute)]
    private partial GpuTexture PageTableMip0 { set; }
    /// <summary>Declares the VgePageRequests StorageBlock slot.</summary>
    [ShaderBinding("VgePageRequests", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    private partial GpuShaderStorageBuffer PageRequests { set; }
    #endregion

    public static string ShaderName => Contract.Identity;

    private const int ParamsUboBinding = GpuBindingRegistry.Ubo.Object; // VGE_UBO_OBJECT_BINDING
    private const int ParamsUboSizeBytes = 16; // uvec4

    private const int PageUsageStampSamplerUnit = 0; // layout(binding=0)
    private const int PageTableMip0SamplerUnit = 1; // layout(binding=1)

    private const int PageRequestCountBindingIndex = 0; // layout(binding=0, offset=0)
    private const int PageRequestsSsboBindingIndex = 0; // layout(std430, binding=0)

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private GpuUniformBuffer? paramsUbo;

    private uint maxRequests;
    private uint frameStamp;
    private uint scanOffset;
    private uint compactMode;

    private readonly GpuComputePipeline pipeline;

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneFeedbackCompactPagesComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

        paramsUbo = GpuUniformBuffer.Create(debugName: "LumOnScene.CompactPages.ParamsUBO");
    }

    private void ApplyParamsUbo()
    {
        paramsUbo ??= GpuUniformBuffer.Create(debugName: "LumOnScene.CompactPages.ParamsUBO");
        UboPacking.WriteUVec4(paramsBytes, 0, maxRequests, frameStamp, scanOffset, compactMode);
        paramsUbo.UploadOrResize(paramsBytes, ParamsUboSizeBytes, growExponentially: false);
        paramsUbo.BindBase(ParamsUboBinding);
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
            maxRequests = value;
            ApplyParamsUbo();
        }
    }

    public uint FrameStamp
    {
        set
        {
            frameStamp = value;
            ApplyParamsUbo();
        }
    }

    public uint ScanOffset
    {
        set
        {
            scanOffset = value;
            ApplyParamsUbo();
        }
    }

    public uint CompactMode
    {
        set
        {
            compactMode = value;
            ApplyParamsUbo();
        }
    }

    public void DispatchBound(int numGroupsX, int numGroupsY, int numGroupsZ) => pipeline.DispatchBound(numGroupsX, numGroupsY, numGroupsZ);

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
            Debug.WriteLine($"[VGE] Exception disposing CompactPages compute shader: {ex}");
        }
    }
}
