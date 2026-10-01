

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Owns the installed binding layout and retained packed inputs.</summary>
internal sealed class LumOnDebugProgramLayout : GpuProgramLayout
{
    public LumOnDebugParamsUbo Params { get; } = new();

    internal LumOnNearFieldVisibilityBindings NearFieldVisibility { get; }

    /// <summary>Consumes the debug shader's generated resource contract and creates its visibility binder.</summary>
    public LumOnDebugProgramLayout()
    {
        RegisterContract(LumOnDebugShaderProgram.WorldProbeIrradianceCombinedContract.Stages[1].Bindings);
        NearFieldVisibility = new LumOnNearFieldVisibilityBindings();

    }

}
