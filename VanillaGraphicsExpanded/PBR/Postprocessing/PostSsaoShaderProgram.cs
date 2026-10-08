using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Declares neutral AO output until the separate occlusion algorithm task is implemented.</summary>
[ShaderProgram("Contract", "pbr_post_ssao", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_post_ssao.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_post_ssao.fsh")]
internal sealed partial class PostSsaoShaderProgram : GpuProgram, IPostSsaoShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the placeholder executable identity.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Registers the procedural geometry and neutral fragment output contracts.</summary>
    public PostSsaoShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    #endregion
}
