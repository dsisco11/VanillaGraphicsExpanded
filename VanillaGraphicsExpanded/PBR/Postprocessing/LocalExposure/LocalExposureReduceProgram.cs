using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.PBR.CameraExposure;
namespace VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
/// <summary>Owns the compiled full-coverage scene-to-log-grid executable.</summary>
[ShaderProgram("Contract","pbr_local_exposure_reduce",1)]
[ShaderStage("Contract",ShaderStageKind.Compute,"pbr_local_exposure_reduce.csh")]
internal sealed partial class LocalExposureReduceProgram : GpuComputeProgram,ILocalExposureReduceBindings
{
    private GpuShaderStorageBuffer? lighting;
    #region Public API
    /// <summary>Adopts the existing typed compute pipeline.</summary>
    public LocalExposureReduceProgram(GpuComputePipeline pipeline) : base(pipeline) { }
    /// <summary>Supplies an explicit frozen storage publication for alternate-view fixtures.</summary>
    internal GpuShaderStorageBuffer? FrameLightingInputs { set { RequireInputMutation(); lighting=value; } }
    /// <summary>Uses the current frozen scale unless the submitting view supplies its own.</summary>
    GpuShaderStorageBuffer IPhotometricFrameBindings.FrameLighting=>lighting??CameraExposureRenderer.FrozenLighting;
    #endregion
}
