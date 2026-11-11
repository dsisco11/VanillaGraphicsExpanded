using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Owns the packaged GPU page table executable.</summary>
[ShaderProgram("Contract", "shadows/pages", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "shadows/pages.csh")]
internal sealed partial class ShadowPageShader : GpuComputeProgram, IShadowPageBindings
{
    #region Public API
    /// <summary>Loads matching packaged SPIR-V and compiler metadata through the existing asset owner.</summary>
    internal static ShadowPageShader Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout(); layout.RegisterContract(Contract.Stages[0].Bindings);
        if (!ComputeShaderAsset.TryCreateFromAssets(api, new ShaderSettings(Contract), out var asset, out var diagnostic, "Shadows.Pages", layout: layout))
            throw new System.InvalidOperationException(diagnostic);
        return new(asset!);
    }
    #endregion
    #region Private
    /// <summary>Adopts the shader asset's reload lifetime.</summary>
    private ShadowPageShader(ComputeShaderAsset asset) : base(asset) { }
    #endregion
}
