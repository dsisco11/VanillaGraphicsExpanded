using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

internal sealed class LumOnScreenProbeAtlasGatherProgramLayout : GpuProgramLayout
{
    internal LumOnNearFieldVisibilityBindings NearFieldVisibility { get; }

    public LumOnScreenProbeAtlasGatherProgramLayout()
    {
        RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumon_probe_atlas_gather"));
        NearFieldVisibility = new LumOnNearFieldVisibilityBindings(this);
    }
}
