using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Specialized shader program for PBR height-bake passes.
/// Automatically registers the shared VgePbrHeightBakeParamsUBO binding.
/// </summary>
internal sealed partial class PbrHeightBakeShaderProgram : GpuProgram
{
    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => System.Linq.Enumerable.Single(Contracts, contract => contract.Identity == PassName);

    private const string ParamsBlockName = "VgePbrHeightBakeParamsUBO";

    /// <summary>Uses the registered height-bake stage pair and binding declarations.</summary>
    public PbrHeightBakeShaderProgram(string passName, string domain)
    {
        PassName = passName;
        AssetDomain = domain;
        ProgramLayout.RegisterContract(ProgramContract.Stages[1].Bindings);

    }
}
