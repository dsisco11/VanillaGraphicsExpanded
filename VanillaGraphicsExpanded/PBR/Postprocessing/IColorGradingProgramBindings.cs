using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Declares the bounded display lookup generator's uniform and image resources.</summary>
internal interface IColorGradingProgramBindings
{
    #region Public API
    /// <summary>Supplies the immutable controls used to bake one candidate.</summary>
    [ShaderBinding("ColorGradingInputs",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.ShaderInputs,ShaderStageKind.Compute)]
    CpuUniformBuffer GradingInputs { set; }
    /// <summary>Supplies the unpublished complete three-dimensional output.</summary>
    [ShaderBinding("gradedOutput",ShaderBindingKind.Image,0,ShaderStageKind.Compute)]
    GpuTextureBinding GradedOutput { set; }
    #endregion
}
