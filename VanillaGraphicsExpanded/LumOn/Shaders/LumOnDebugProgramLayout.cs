using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

internal sealed class LumOnDebugProgramLayout : GpuProgramLayout
{
    public LumOnDebugParamsUbo Params { get; } = new();

    internal LumOnLocalVisibilityBindings LocalVisibility { get; }

    public LumOnDebugProgramLayout()
    {
        LocalVisibility = new LumOnLocalVisibilityBindings(this, 34, 35);
        RegisterUniformBlockBinding(LumOnUniformBuffers.FrameBlockName, LumOnUniformBuffers.FrameBinding, required: true);
        RegisterUniformBlockBinding(LumOnUniformBuffers.WorldProbeBlockName, LumOnUniformBuffers.WorldProbeBinding, required: false);
        RegisterUniformBlockBinding(LumOnTerrainBridgeUboState.BlockName, LumOnTerrainBridgeUboState.Binding, required: false);
        RegisterUniformBlockBinding(LumOnDebugParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object, required: true);

        // Fixed sampler-unit contract. Mark many as optional because debug modes are define-gated
        // and compilers may optimize unused uniforms away.

        RegisterSamplerUnit("primaryDepth", 0, required: true);
        RegisterSamplerUnit("gBufferNormal", 1, required: true);
        RegisterSamplerUnit("probeAnchorPosition", 2, required: false);
        RegisterSamplerUnit("probeAnchorNormal", 3, required: false);
        RegisterSamplerUnit("radianceTexture0", 4, required: false);
        RegisterSamplerUnit("radianceTexture1", 5, required: false);
        RegisterSamplerUnit("indirectHalf", 6, required: false);
        RegisterSamplerUnit("historyMeta", 7, required: false);
        RegisterSamplerUnit("probeAtlasMeta", 8, required: false);
        RegisterSamplerUnit("probeAtlasCurrent", 9, required: false);
        RegisterSamplerUnit("probeAtlasFiltered", 10, required: false);
        RegisterSamplerUnit("probeAtlasGatherInput", 11, required: false);
        RegisterSamplerUnit("indirectDiffuseFull", 12, required: false);
        RegisterSamplerUnit("gBufferAlbedo", 13, required: false);
        RegisterSamplerUnit("gBufferMaterial", 14, required: false);
        RegisterSamplerUnit("directDiffuse", 15, required: false);
        RegisterSamplerUnit("directSpecular", 16, required: false);
        RegisterSamplerUnit("emissive", 17, required: false);
        RegisterSamplerUnit("velocityTex", 18, required: false);

        RegisterSamplerUnit("worldProbeRadianceAtlas", 19, required: false);
        RegisterSamplerUnit("vge_traceOccL0", 20, required: false);
        RegisterSamplerUnit("worldProbeSuppressedLighting", 21, required: false);
        RegisterSamplerUnit("worldProbeVis0", 22, required: false);
        RegisterSamplerUnit("worldProbeDist0", 23, required: false);
        RegisterSamplerUnit("worldProbeMeta0", 24, required: false);
        RegisterSamplerUnit("worldProbeDebugState0", 25, required: false);

        RegisterSamplerUnit("probeTraceMask", 26, required: false);
        RegisterSamplerUnit("probeAtlasTrace", 27, required: false);
        RegisterSamplerUnit("probePisEnergy", 28, required: false);
        RegisterSamplerUnit("gBufferPatchId", 29, required: false);

        RegisterSamplerUnit("vge_lumonScenePageTableMip0", 30, required: false);
        RegisterSamplerUnit("vge_lumonSceneIrradianceAtlas", 31, required: false);
        RegisterSamplerUnit("vge_lumonSceneMaterialAtlas", 32, required: false);
        RegisterSamplerUnit("vge_lumonSceneSurfaceLut", 33, required: false);
    }

    public void BindParamsUbo(GpuProgram program, string debugName)
    {
        Params.BindTo(program, LumOnDebugParamsUbo.BlockName, debugName);
    }

    public void BindTexture2D(int programId, string uniformName, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, uniformName, TextureTarget.Texture2D, textureId, samplerId: 0, warn);

    public void BindNearestClamp2D(int programId, string uniformName, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, uniformName, TextureTarget.Texture2D, textureId, GpuSamplers.NearestClamp.SamplerId, warn);

    public void BindTexture3D(int programId, string uniformName, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, uniformName, TextureTarget.Texture3D, textureId, samplerId: 0, warn);
}
