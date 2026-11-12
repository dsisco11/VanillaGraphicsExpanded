using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Loads the packaged candidate-to-view selection executable through the ordinary shader owner.</summary>
[ShaderProgram("Contract", "shadows/select", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "shadows/select.csh")]
internal sealed partial class ShadowSelectionShader : GpuComputeProgram, IShadowSelectionBindings
{
    #region Public API
    /// <summary>Requires matching compiled SPIR-V and generated interface metadata.</summary>
    internal static ShadowSelectionShader Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(Contract.Stages[0].Bindings);
        if (!ComputeShaderAsset.TryCreateFromAssets(api, new ShaderSettings(Contract), out var asset,
            out var diagnostic, "Shadows.Selection", layout: layout))
            throw new InvalidOperationException(diagnostic);
        return new(asset!);
    }
    #endregion

    #region Private
    /// <summary>Adopts the executable's existing reload and resource lifetime.</summary>
    private ShadowSelectionShader(ComputeShaderAsset asset) : base(asset) { }
    #endregion
}
