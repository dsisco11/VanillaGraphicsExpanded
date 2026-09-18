using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

internal sealed class LumOnScreenProbeAtlasTraceProgramLayout : GpuProgramLayout
{
    public LumOnScreenProbeAtlasTraceProgramLayout()
    {
        RegisterSamplerUnit("probeAnchorPosition", 0, required: true);
        RegisterSamplerUnit("probeAnchorNormal", 1, required: true);
        RegisterSamplerUnit("primaryDepth", 2, required: true);
        RegisterSamplerUnit("directDiffuse", 3, required: true);
        RegisterSamplerUnit("emissive", 4, required: true);
        RegisterSamplerUnit("octahedralHistory", 5, required: true);
        RegisterSamplerUnit("hzbDepth", 6, required: true);
        RegisterSamplerUnit("probeAtlasMetaHistory", 7, required: true);
        RegisterSamplerUnit("worldProbeRadianceAtlas", 8, required: false);
        RegisterSamplerUnit("probeTraceMask", 9, required: true);
        RegisterSamplerUnit("worldProbeVis0", 11, required: false);
        RegisterSamplerUnit("worldProbeMeta0", 12, required: false);
    }
}