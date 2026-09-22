using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

internal sealed class PbrDirectLightingProgramLayout : GpuProgramLayout
{
    public PbrDirectLightingParamsUbo Params { get; } = new();

    public PbrDirectLightingProgramLayout()
    {
        RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("pbr_direct_lighting"));
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
