using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

internal sealed class LumOnProbeSh9GatherProgramLayout : GpuProgramLayout
{
    internal LumOnNearFieldVisibilityBindings NearFieldVisibility { get; }

    public LumOnProbeSh9GatherProgramLayout()
    {
        RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumon_probe_sh9_gather"));
        NearFieldVisibility = new LumOnNearFieldVisibilityBindings(this);
    }
}
