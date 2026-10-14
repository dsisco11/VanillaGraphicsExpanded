using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns the offline-built compute executable that generates a display lookup.</summary>
[ShaderProgram("Contract","pbr_grading_lut",1)]
[ShaderStage("Contract",ShaderStageKind.Compute,"pbr_grading_lut.csh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations),Defaults=true)]
internal sealed partial class ColorGradingProgram : GpuComputeProgram,IColorGradingProgramBindings
{
    #region Public API
    /// <summary>Adopts the existing compute pipeline and generated resource contract.</summary>
    public ColorGradingProgram(GpuComputePipeline pipeline) : base(pipeline) { }
    #endregion
}
