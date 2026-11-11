using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Owns selective replacement initialization through the packaged shader catalog.</summary>
[ShaderProgram("Contract", "shadows/clear", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "shadows/clear.csh")]
internal sealed partial class ShadowTileClearShader : GpuComputeProgram, IShadowTileClearBindings
{
    #region Public API
    /// <summary>Loads exact compiled code and metadata using the existing compute asset owner.</summary>
    internal static ShadowTileClearShader Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(Contract.Stages[0].Bindings);
        if (!ComputeShaderAsset.TryCreateFromAssets(api, new ShaderSettings(Contract), out var asset,
            out var diagnostic, "Shadows.Clear", layout: layout))
            throw new InvalidOperationException(diagnostic);
        return new(asset!);
    }
    #endregion

    #region Private
    /// <summary>Adopts the existing shader asset lifetime without independent executable ownership.</summary>
    private ShadowTileClearShader(ComputeShaderAsset asset) : base(asset) { }
    #endregion
}
