using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns candidate expansion, keyed deduplication, prefix compaction and initialized indirect commands.</summary>
[ShaderProgram("Contract", "shadows/expand", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "shadows/expand.csh")]
internal sealed partial class ShadowExpansionShader : GpuComputeProgram, IShadowExpansionBindings
{
    #region Public API
    /// <summary>Loads the catalog-validated executable through the GL 4.5 GLSL path.</summary>
    internal static ShadowExpansionShader Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(Contract.Stages[0].Bindings);
        return new(ComputeShaderAsset.CreateBaselineFromAssets(api, new ShaderSettings(Contract), layout, "Shadows.Expand"));
    }
    #endregion

    #region Private
    /// <summary>Adopts the existing shader asset's preparation and replacement lifetime.</summary>
    private ShadowExpansionShader(ComputeShaderAsset asset) : base(asset) { }
    #endregion
}
