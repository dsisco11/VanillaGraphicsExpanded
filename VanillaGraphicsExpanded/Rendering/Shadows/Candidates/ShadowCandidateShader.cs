using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>Owns the generated binding contract and baseline candidate compute executable.</summary>
[ShaderProgram("Contract", "shadows/candidates", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "shadows/candidates.csh")]
internal sealed partial class ShadowCandidateShader : GpuComputeProgram, IShadowCandidateBindings
{
    #region Public API
    /// <summary>Loads the catalog-validated GLSL source through RHI without optional SPIR-V ingestion.</summary>
    internal static ShadowCandidateShader Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout(); layout.RegisterContract(Contract.Stages[0].Bindings);
        return new(ComputeShaderAsset.CreateBaselineFromAssets(api, new ShaderSettings(Contract), layout, "Shadows.Candidates"));
    }
    #endregion
    #region Private
    /// <summary>Adopts the prepared executable and its existing reload lifetime.</summary>
    private ShadowCandidateShader(ComputeShaderAsset pipeline) : base(pipeline) { }
    #endregion
}
