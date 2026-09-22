using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

internal sealed class LumOnCombineProgramLayout : GpuProgramLayout
{
    public const string FrameBlockName = LumOnUniformBuffers.FrameBlockName;

    public LumOnCombineParamsUbo Params { get; } = new();

    public LumOnCombineProgramLayout()
    {
        RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumon_combine"));
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
