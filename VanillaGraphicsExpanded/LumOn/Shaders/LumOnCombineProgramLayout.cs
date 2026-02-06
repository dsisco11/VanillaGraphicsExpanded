using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

internal sealed class LumOnCombineProgramLayout : GpuProgramLayout
{
    public const string FrameBlockName = "LumOnFrameUBO";

    public LumOnCombineParamsUbo Params { get; } = new();

    public LumOnCombineProgramLayout()
    {
        RegisterUniformBlockBinding(FrameBlockName, LumOnUniformBuffers.FrameBinding, required: true);
        RegisterUniformBlockBinding(LumOnCombineParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object, required: true);

        // Sampler units are part of the shader contract and should be stable across frames.
        RegisterSamplerUnit("sceneDirect", 0, required: true);
        RegisterSamplerUnit("indirectDiffuse", 1, required: true);
        RegisterSamplerUnit("gBufferAlbedo", 2, required: true);
        RegisterSamplerUnit("gBufferMaterial", 3, required: true);
        RegisterSamplerUnit("primaryDepth", 4, required: true);
        RegisterSamplerUnit("gBufferNormal", 5, required: true);
    }

    public void BindParamsUbo(GpuProgram program, string debugName)
    {
        Params.BindTo(program, LumOnCombineParamsUbo.BlockName, debugName);
    }

    public void BindSceneDirect(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "sceneDirect", TextureTarget.Texture2D, textureId, samplerId: 0, warn);

    public void BindIndirectDiffuse(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "indirectDiffuse", TextureTarget.Texture2D, textureId, samplerId: 0, warn);

    public void BindGBufferAlbedo(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "gBufferAlbedo", TextureTarget.Texture2D, textureId, GpuSamplers.NearestClamp.SamplerId, warn);

    public void BindGBufferMaterial(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "gBufferMaterial", TextureTarget.Texture2D, textureId, GpuSamplers.NearestClamp.SamplerId, warn);

    public void BindPrimaryDepth(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "primaryDepth", TextureTarget.Texture2D, textureId, GpuSamplers.NearestClamp.SamplerId, warn);

    public void BindGBufferNormal(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "gBufferNormal", TextureTarget.Texture2D, textureId, GpuSamplers.NearestClamp.SamplerId, warn);
}
