using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
/// <summary>Owns the compiled current-frame bilateral base executable.</summary>
[ShaderProgram("Contract","pbr_local_exposure_filter",1)]
[ShaderStage("Contract",ShaderStageKind.Compute,"pbr_local_exposure_filter.csh")]
internal sealed partial class LocalExposureFilterProgram : GpuComputeProgram,ILocalExposureFilterBindings
{
    #region Public API
    /// <summary>Adopts the existing typed compute pipeline.</summary>
    public LocalExposureFilterProgram(ComputeShaderAsset pipeline) : base(pipeline) { }
    #endregion
}
