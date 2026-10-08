using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns the pbr_godrays executable and its typed staged inputs.</summary>
[ShaderProgram("Contract", "pbr_godrays", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_godrays.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_godrays.fsh")]
internal sealed partial class GodRayShaderProgram : GpuProgram, IGodRayShaderProgramBindings
{
    private readonly PostprocessInputs inputs;
    #region Public API
    /// <summary>Declares compilation and reload identity.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Creates the retained parameter block and generated binding layout.</summary>
    public GodRayShaderProgram() {
        inputs = OwnUniformBuffer(new PostprocessInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Stages the VisibilityImage image.</summary>
    public partial GpuTexture? VisibilityImage { set; }
    /// <summary>Stages the DepthImage image.</summary>
    public partial GpuTexture? DepthImage { set; }
    /// <summary>Captures the complete draw parameters for the shader contract.</summary>
    internal void Capture(Vector4 pass, Vector4 effect, Vector4 sun = default, Vector4 solar = default)
        => inputs.Capture(pass, effect, sun, solar);
    /// <summary>Publishes the retained input block.</summary>
    CpuUniformBuffer IGodRayShaderProgramBindings.Inputs => inputs;
    #endregion
}
