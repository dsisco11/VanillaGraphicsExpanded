using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Shaders;

public sealed class LumonSceneFeedbackCompactPagesShader : IDisposable
{
    private const string ShaderFileName = "lumonscene_feedback_compact_pages.csh";

    private const int PageUsageStampSamplerUnit = 0; // layout(binding=0)
    private const int PageTableMip0SamplerUnit = 1; // layout(binding=1)

    private const int MaxRequestsLocation = 0;
    private const int FrameStampLocation = 1;
    private const int ScanOffsetLocation = 2;
    private const int CompactModeLocation = 3;

    private readonly ComputeProgram _program;

    public int ProgramId => _program.ProgramId;

    public LumonSceneFeedbackCompactPagesShader(ShaderTestHelper helper, string? debugName = null)
    {
        _program = ComputeProgram.Create(helper, ShaderFileName, debugName: debugName);
    }

    public void Use() => GL.UseProgram(ProgramId);

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

    public void Dispose() => _program.Dispose();
}
