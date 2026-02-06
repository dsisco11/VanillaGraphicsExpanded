using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

internal sealed class LumOnCombineProgramLayout : GpuProgramLayout
{
    public const string FrameBlockName = "LumOnFrameUBO";

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
}
