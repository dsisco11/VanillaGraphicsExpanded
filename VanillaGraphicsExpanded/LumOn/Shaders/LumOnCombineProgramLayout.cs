

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Owns the installed binding layout and retained packed inputs.</summary>
internal sealed class LumOnCombineProgramLayout : GpuProgramLayout
{
    public const string FrameBlockName = LumOnUniformBuffers.FrameBlockName;

    public LumOnCombineParamsUbo Params { get; } = new();

    public LumOnCombineProgramLayout()
    {
        RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumon_combine"));
    }

}
