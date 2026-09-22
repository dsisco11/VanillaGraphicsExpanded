using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

internal sealed class LumOnScreenProbeAtlasTraceProgramLayout : GpuProgramLayout
{
    public LumOnScreenProbeAtlasTraceProgramLayout()
    {
        RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumon_probe_atlas_trace"));
    }
}
