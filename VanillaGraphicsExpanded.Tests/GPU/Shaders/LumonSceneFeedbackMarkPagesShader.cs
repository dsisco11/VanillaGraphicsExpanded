using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Shaders;

public sealed class LumonSceneFeedbackMarkPagesShader : IDisposable
{
    private const string ShaderFileName = "lumonscene_feedback_mark_pages.csh";

    private const int PatchIdGBufferSamplerUnit = 0; // layout(binding=0)
    private const int ChunkSlotGenerationSamplerUnit = 1; // layout(binding=1)

    private const int PageUsageStampImageUnit = 0; // layout(binding=0, r32ui)

    private const string ParamsBlockName = "VgeLumOnSceneFeedbackMarkParamsUBO";
    private const int ParamsUboSizeBytes = 16; // std140 uvec4

    private static readonly GpuProgramLayout Layout = CreateLayout();

    private readonly ComputeProgram _program;
    private readonly byte[] _paramsBytes = new byte[ParamsUboSizeBytes];
    private GpuUniformBuffer? _paramsUbo;

    public int ProgramId => _program.ProgramId;

    public LumonSceneFeedbackMarkPagesShader(ShaderTestHelper helper, string? debugName = null)
    {
        _program = ComputeProgram.Create(helper, ShaderFileName, debugName: debugName, layout: Layout);
    }

    public void Use() => GL.UseProgram(ProgramId);

    public uint FrameStamp
    {
        set
        {
            UboPacking.WriteUVec4(_paramsBytes, 0, value, 0u, 0u, 0u);
            UploadAndBindParamsUbo();
        }
    }

    public void BindPatchIdGBuffer(int textureId)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + PatchIdGBufferSamplerUnit);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    public void BindChunkSlotGenerationTex(int textureId)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + ChunkSlotGenerationSamplerUnit);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    public void BindPageUsageStampImage(int textureId, TextureAccess access = TextureAccess.ReadWrite)
    {
        GL.BindImageTexture(
            unit: PageUsageStampImageUnit,
            texture: textureId,
            level: 0,
            layered: true,
            layer: 0,
            access: access,
            format: SizedInternalFormat.R32ui);
    }

    public void Dispose()
    {
        _paramsUbo?.Dispose();
        _paramsUbo = null;
        _program.Dispose();
    }

    private void UploadAndBindParamsUbo()
    {
        if (_paramsUbo is null || _paramsUbo.BufferId == 0)
        {
            _paramsUbo?.Dispose();
            _paramsUbo = GpuUniformBuffer.Create(debugName: "Tests.LumOnScene.FeedbackMark.ParamsUBO");
        }

        _paramsUbo.UploadOrResize(_paramsBytes, ParamsUboSizeBytes, growExponentially: false);
        _paramsUbo.BindBase(GpuBindingRegistry.Ubo.Object);
    }

    private static GpuProgramLayout CreateLayout()
    {
        var layout = new GpuProgramLayout();
        layout.RegisterUniformBlockBinding(ParamsBlockName, GpuBindingRegistry.Ubo.Object, required: true);
        layout.RegisterSamplerUnit("vge_patchIdGBuffer", PatchIdGBufferSamplerUnit, required: true);
        layout.RegisterSamplerUnit("vge_chunkSlotGenerationTex", ChunkSlotGenerationSamplerUnit, required: false);
        layout.RegisterImageUnit("vge_pageUsageStamp", PageUsageStampImageUnit, required: true);
        return layout;
    }
}
