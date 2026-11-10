using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Owns the baseline GLSL GPU page table executable.</summary>
[ShaderProgram("Contract", "shadows/pages", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "shadows/pages.csh")]
internal sealed partial class ShadowPageShader : GpuComputeProgram, IShadowPageBindings
{
    #region Public API
    /// <summary>Loads the catalog-validated shader through baseline RHI GLSL creation.</summary>
    internal static ShadowPageShader Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout(); layout.RegisterContract(Contract.Stages[0].Bindings);
        return new(ComputeShaderAsset.CreateBaselineFromAssets(api, new ShaderSettings(Contract), layout, "Shadows.Pages"));
    }
    #endregion
    #region Private
    /// <summary>Adopts the shader asset's reload lifetime.</summary>
    private ShadowPageShader(ComputeShaderAsset asset) : base(asset) { }
    #endregion
}
