using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Shaders;

public sealed class LumonSceneFeedbackCompactPagesShader : IDisposable
{
    private const string ShaderFileName = "lumonscene_feedback_compact_pages.csh";

    private const int PageUsageStampSamplerUnit = 0; // layout(binding=0)
    private const int PageTableMip0SamplerUnit = 1; // layout(binding=1)

    private const string ParamsBlockName = "VgeLumOnSceneFeedbackCompactParamsUBO";
    private const int ParamsUboSizeBytes = 16; // std140 uvec4

    private static readonly GpuProgramLayout Layout = CreateLayout();

    private readonly ComputeProgram _program;
    private readonly byte[] _paramsBytes = new byte[ParamsUboSizeBytes];
    private GpuUniformBuffer? _paramsUbo;

    private uint _maxRequests;
    private uint _frameStamp;
    private uint _scanOffset;
    private uint _compactMode;

    public int ProgramId => _program.ProgramId;

    public LumonSceneFeedbackCompactPagesShader(ShaderTestHelper helper, string? debugName = null)
    {
        _program = ComputeProgram.Create(helper, ShaderFileName, debugName: debugName, layout: Layout);
    }

    public void Use() => GL.UseProgram(ProgramId);

    public uint MaxRequests
    {
        set
        {
            _maxRequests = value;
            UploadAndBindParamsUbo();
        }
    }

    public uint FrameStamp
    {
        set
        {
            _frameStamp = value;
            UploadAndBindParamsUbo();
        }
    }

    public uint ScanOffset
    {
        set
        {
            _scanOffset = value;
            UploadAndBindParamsUbo();
        }
    }

    public uint CompactMode
    {
        set
        {
            _compactMode = value;
            UploadAndBindParamsUbo();
        }
    }

    public void BindPageUsageStamp(int textureId)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + PageUsageStampSamplerUnit);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    public void BindPageTableMip0(int textureId)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + PageTableMip0SamplerUnit);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.ActiveTexture(TextureUnit.Texture0);
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
            _paramsUbo = GpuUniformBuffer.Create(debugName: "Tests.LumOnScene.FeedbackCompact.ParamsUBO");
        }

        UboPacking.WriteUVec4(_paramsBytes, 0, _maxRequests, _frameStamp, _scanOffset, _compactMode);
        _paramsUbo.UploadOrResize(_paramsBytes, ParamsUboSizeBytes, growExponentially: false);
        _paramsUbo.BindBase(GpuBindingRegistry.Ubo.Object);
    }

    private static GpuProgramLayout CreateLayout()
    {
        var layout = new GpuProgramLayout();
        layout.RegisterUniformBlockBinding(ParamsBlockName, GpuBindingRegistry.Ubo.Object, required: true);
        layout.RegisterSamplerUnit("vge_pageUsageStamp", PageUsageStampSamplerUnit, required: true);
        layout.RegisterSamplerUnit("vge_pageTableMip0", PageTableMip0SamplerUnit, required: true);
        layout.RegisterShaderStorageBlockBinding("VgePageRequests", bindingIndex: 0, required: true);
        return layout;
    }
}
