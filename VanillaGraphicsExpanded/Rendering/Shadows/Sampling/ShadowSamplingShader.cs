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
    /// <summary>Loads matching packaged SPIR-V and compiler metadata through the existing asset owner.</summary>
    internal static ShadowSamplingShader Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout(); layout.RegisterContract(Contract.Stages[0].Bindings);
        if (!ComputeShaderAsset.TryCreateFromAssets(api, new ShaderSettings(Contract), out var asset, out var diagnostic, "Shadows.Sampling", layout: layout))
            throw new System.InvalidOperationException(diagnostic);
        return new(asset!);
    }
    #endregion
    #region Private
    /// <summary>Adopts the existing shader asset owner.</summary>
    private ShadowSamplingShader(ComputeShaderAsset asset) : base(asset) { }
    #endregion
}
