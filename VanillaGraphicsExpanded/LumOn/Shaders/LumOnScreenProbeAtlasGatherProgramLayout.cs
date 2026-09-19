using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

internal sealed class LumOnScreenProbeAtlasGatherProgramLayout : GpuProgramLayout
{
    public LumOnScreenProbeAtlasGatherProgramLayout()
    {
        RegisterSamplerUnit("octahedralAtlas", 0, required: true);
        RegisterSamplerUnit("probeAnchorPosition", 1, required: true);
        RegisterSamplerUnit("probeAnchorNormal", 2, required: true);
        RegisterSamplerUnit("primaryDepth", 3, required: true);
        RegisterSamplerUnit("gBufferNormal", 4, required: true);
        RegisterSamplerUnit("worldProbeRadianceAtlas", 5, required: false);
        RegisterSamplerUnit("worldProbeVis0", 8, required: false);
        RegisterSamplerUnit("worldProbeMeta0", 9, required: false);
    }
}