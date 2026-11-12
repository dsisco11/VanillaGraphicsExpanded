using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Readiness;

/// <summary>Packaged compute reduction reusing the shared sampling coverage and temporal contract.</summary>
[ShaderProgram("Contract", "shadows/readiness", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "shadows/readiness.csh")]
internal sealed partial class ShadowReadinessShader : GpuComputeProgram, IShadowReadinessBindings
{
    #region Public API
    /// <summary>Loads exact precompiled SPIR-V through the existing shader asset owner.</summary>
    internal static ShadowReadinessShader Create(ICoreAPI api)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!ComputeShaderAsset.TryCreateFromAssets(api,new ShaderSettings(Contract),out var asset,out var diagnostic,"Shadows.Readiness",layout:layout))
            throw new System.InvalidOperationException(diagnostic);
        return new(asset!);
    }
    #endregion
    #region Private
    /// <summary>Adopts the existing shader lifetime.</summary>
    private ShadowReadinessShader(ComputeShaderAsset asset):base(asset) { }
    #endregion
}
