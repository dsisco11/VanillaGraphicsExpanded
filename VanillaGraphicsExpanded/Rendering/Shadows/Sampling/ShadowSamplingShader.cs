using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Sampling;

/// <summary>Baseline compute entry point exercising the shared demand/address/filter implementation.</summary>
[ShaderProgram("Contract", "shadows/sample", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "shadows/sample.csh")]
internal sealed partial class ShadowSamplingShader : GpuComputeProgram, IShadowSamplingBindings
{
    #region Public API
    /// <summary>Loads the same catalog-validated GLSL source used by the baseline shared sampler.</summary>
    internal static ShadowSamplingShader Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout(); layout.RegisterContract(Contract.Stages[0].Bindings);
        return new(ComputeShaderAsset.CreateBaselineFromAssets(api, new ShaderSettings(Contract), layout, "Shadows.Sampling"));
    }
    #endregion
    #region Private
    /// <summary>Adopts the existing shader asset owner.</summary>
    private ShadowSamplingShader(ComputeShaderAsset asset) : base(asset) { }
    #endregion
}
