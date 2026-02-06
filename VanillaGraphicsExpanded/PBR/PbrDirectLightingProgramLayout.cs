using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

internal sealed class PbrDirectLightingProgramLayout : GpuProgramLayout
{
    public PbrDirectLightingParamsUbo Params { get; } = new();

    public PbrDirectLightingProgramLayout()
    {
        RegisterUniformBlockBinding(PbrDirectLightingParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object, required: true);

        RegisterSamplerUnit("primaryScene", unit: 0, required: true);
        RegisterSamplerUnit("primaryDepth", unit: 1, required: true);
        RegisterSamplerUnit("gBufferNormal", unit: 2, required: true);
        RegisterSamplerUnit("gBufferMaterial", unit: 3, required: true);
        RegisterSamplerUnit("shadowMapNear", unit: 4, required: true);
        RegisterSamplerUnit("shadowMapFar", unit: 5, required: true);
    }

    public void BindParamsUbo(Rendering.Shaders.GpuProgram program, string debugName)
    {
        Params.BindTo(program, PbrDirectLightingParamsUbo.BlockName, debugName);
    }

    public void BindPrimaryScene(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "primaryScene", TextureTarget.Texture2D, textureId, GpuSamplers.LinearClamp.SamplerId, warn);

    public void BindPrimaryDepth(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "primaryDepth", TextureTarget.Texture2D, textureId, GpuSamplers.NearestClamp.SamplerId, warn);

    public void BindGBufferNormal(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "gBufferNormal", TextureTarget.Texture2D, textureId, GpuSamplers.NearestClamp.SamplerId, warn);

    public void BindGBufferMaterial(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "gBufferMaterial", TextureTarget.Texture2D, textureId, GpuSamplers.NearestClamp.SamplerId, warn);

    public void BindShadowMapNear(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "shadowMapNear", TextureTarget.Texture2D, textureId, GpuSamplers.ShadowCompareLinearClamp.SamplerId, warn);

    public void BindShadowMapFar(int programId, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, "shadowMapFar", TextureTarget.Texture2D, textureId, GpuSamplers.ShadowCompareLinearClamp.SamplerId, warn);
}
