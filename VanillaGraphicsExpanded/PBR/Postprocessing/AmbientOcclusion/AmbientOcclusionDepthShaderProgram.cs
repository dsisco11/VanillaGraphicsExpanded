using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns the pbr_ao_depth executable and typed retained inputs.</summary>
[ShaderProgram("Contract", "pbr_ao_depth", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_ao_depth.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_ao_depth.fsh")]
internal sealed partial class AmbientOcclusionDepthShaderProgram : GpuProgram, IAmbientOcclusionDepthShaderProgramBindings
{
    private readonly PostprocessInputs inputs;
    #region Public API
    /// <summary>Declares executable and reload identity.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Registers bindings and owns their retained parameter storage.</summary>
    public AmbientOcclusionDepthShaderProgram() {
        inputs=OwnUniformBuffer(new PostprocessInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Stages sourceImage without transferring ownership.</summary>
    public partial GpuTexture? SourceImage { set; }
    /// <summary>Stages the complete operation parameters before submission.</summary>
    internal void Capture(bool reduced)
        => inputs.Capture(new(0,0,reduced?1:0,0),Vector4.Zero);
    /// <summary>Publishes the retained uniform block through the generated binding layout.</summary>
    CpuUniformBuffer IAmbientOcclusionDepthShaderProgramBindings.Inputs => inputs;
    #endregion
}
