using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns the executable and retained level block for all-minimum child membership.</summary>
[ShaderProgram("Contract", "pbr_ao_minimum_children", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_ao_minimum_children.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_ao_minimum_children.fsh")]
internal sealed partial class MinimumChildMaskShaderProgram : GpuProgram, IMinimumChildMaskShaderProgramBindings
{
    private readonly PackedUniformBuffer parameters = new(16);
    private readonly byte[] bytes = new byte[16];
    #region Public API
    /// <summary>Selects the typed mask executable.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Registers resource contracts and adopts persistent per-level parameters.</summary>
    public MinimumChildMaskShaderProgram()
    {
        OwnUniformBuffer(parameters);
        foreach(var stage in Contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }
    /// <summary>Borrows the completed shared depth chain.</summary>
    public partial GpuTextureRef Hierarchy { set; }
    /// <summary>Stages the parent level without allocating parameters during rendering.</summary>
    internal void Capture(int level)
    {
        RequireInputMutation();
        UboPacking.WriteIVec4(bytes,0,level,0,0,0); parameters.SetBytes(bytes);
    }
    /// <summary>Publishes the retained block through generated bindings.</summary>
    CpuUniformBuffer IMinimumChildMaskShaderProgramBindings.Parameters => parameters;
    #endregion
}
