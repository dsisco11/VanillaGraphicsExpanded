using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns SSAO submission while retaining the installed receiver algorithm.</summary>
[ShaderProgram("Contract", "pbr_post_ssao", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_post_ssao.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_post_ssao.fsh")]
internal sealed partial class PostSsaoShaderProgram : GpuProgram, IPostSsaoShaderProgramBindings {
    private readonly SsaoInputs inputs;
    #region Public API
    /// <summary>Declares the executable identity.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Creates retained kernel storage and its generated bindings.</summary>
    public PostSsaoShaderProgram() {
        inputs=OwnUniformBuffer(new SsaoInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Samples receiver normals.</summary>
    public partial GpuTexture? Normal { set; }
    /// <summary>Samples receiver positions.</summary>
    public partial GpuTexture? Position { set; }
    /// <summary>Samples transparent revealage.</summary>
    public partial GpuTexture? Revealage { set; }
    /// <summary>Stages projection, quality and engine kernel through the typed contract.</summary>
    internal void Capture(float[] projection,int width,int height,int quality,float[] kernel)
        => inputs.Capture(projection,width,height,quality,kernel);
    /// <summary>Publishes the retained input block.</summary>
    CpuUniformBuffer IPostSsaoShaderProgramBindings.Inputs => inputs;
    #endregion
}
