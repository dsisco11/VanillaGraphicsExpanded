using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Shaders;

public sealed class LumonSceneFeedbackMarkPagesShader : IDisposable
{
    private const string ShaderFileName = "lumonscene_feedback_mark_pages.csh";

    private const int PatchIdGBufferSamplerUnit = 0; // layout(binding=0)
    private const int ChunkSlotGenerationSamplerUnit = 1; // layout(binding=1)

    private const int PageUsageStampImageUnit = 0; // layout(binding=0, r32ui)

    private const int FrameStampLocation = 0; // layout(location=0)

    private readonly ComputeProgram _program;

    public int ProgramId => _program.ProgramId;

    public LumonSceneFeedbackMarkPagesShader(ShaderTestHelper helper, string? debugName = null)
    {
        _program = ComputeProgram.Create(helper, ShaderFileName, debugName: debugName);
    }

    public void Use() => GL.UseProgram(ProgramId);

    public uint FrameStamp
    {
        set
        {
            Use();
            GL.Uniform1(FrameStampLocation, value);
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

    public void Dispose() => _program.Dispose();
}
