using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR;

internal sealed class PbrCompositeProgramLayout : GpuProgramLayout
{
    public PbrCompositeParamsUbo Params { get; } = new();

    public PbrCompositeProgramLayout()
    {
        RegisterUniformBlockBinding(PbrCompositeParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object, required: true);

        RegisterSamplerUnit("directDiffuse", unit: 0, required: true);
        RegisterSamplerUnit("directSpecular", unit: 1, required: true);
        RegisterSamplerUnit("emissive", unit: 2, required: true);
        RegisterSamplerUnit("indirectDiffuse", unit: 3, required: true);

        RegisterSamplerUnit("gBufferAlbedo", unit: 4, required: true);
        RegisterSamplerUnit("gBufferMaterial", unit: 5, required: true);
        RegisterSamplerUnit("primaryDepth", unit: 6, required: true);
        RegisterSamplerUnit("gBufferNormal", unit: 7, required: true);
    }

    public void BindParamsUbo(GpuProgram program, string debugName)
    {
        Params.BindTo(program, PbrCompositeParamsUbo.BlockName, debugName);
    }

    public void BindDirectDiffuse(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "directDiffuse", TextureTarget.Texture2D, textureId, samplerId: 0, warn);

    public void BindDirectSpecular(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "directSpecular", TextureTarget.Texture2D, textureId, samplerId: 0, warn);

    public void BindEmissive(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "emissive", TextureTarget.Texture2D, textureId, samplerId: 0, warn);

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
