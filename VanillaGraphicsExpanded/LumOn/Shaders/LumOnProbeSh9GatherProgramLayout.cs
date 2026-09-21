using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

internal sealed class LumOnProbeSh9GatherProgramLayout : GpuProgramLayout
{
    internal LumOnNearFieldVisibilityBindings NearFieldVisibility { get; }

    public LumOnProbeSh9GatherProgramLayout()
    {
        NearFieldVisibility = new LumOnNearFieldVisibilityBindings(this, 12, 13);
        RegisterSamplerUnit("probeSh0", 0, required: true);
        RegisterSamplerUnit("probeSh1", 1, required: true);
        RegisterSamplerUnit("probeSh2", 2, required: true);
        RegisterSamplerUnit("probeSh3", 3, required: true);
        RegisterSamplerUnit("probeSh4", 4, required: true);
        RegisterSamplerUnit("probeSh5", 5, required: true);
        RegisterSamplerUnit("probeSh6", 6, required: true);
        RegisterSamplerUnit("probeAnchorPosition", 7, required: true);
        RegisterSamplerUnit("probeAnchorNormal", 8, required: true);
        RegisterSamplerUnit("primaryDepth", 9, required: true);
        RegisterSamplerUnit("gBufferNormal", 10, required: true);
        RegisterSamplerUnit("worldProbeRadianceAtlas", 11, required: false);
        RegisterSamplerUnit("worldProbeVis0", 14, required: false);
        RegisterSamplerUnit("worldProbeMeta0", 15, required: false);
    }
}