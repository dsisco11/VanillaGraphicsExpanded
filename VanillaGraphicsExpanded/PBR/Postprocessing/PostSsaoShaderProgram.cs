using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns the pbr_post_ssao executable and typed retained inputs.</summary>
[ShaderProgram("Contract", "pbr_post_ssao", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_post_ssao.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_post_ssao.fsh")]
internal sealed partial class PostSsaoShaderProgram : GpuProgram, IPostSsaoShaderProgramBindings
{
    private readonly AmbientOcclusionInputs inputs;
    private VgeFrameUniformBuffer? frameInputs;
    #region Public API
    /// <summary>Declares executable and reload identity.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Registers bindings and owns their retained parameter storage.</summary>
    public PostSsaoShaderProgram() {
        inputs=OwnUniformBuffer(new AmbientOcclusionInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Stages depthImage without transferring ownership.</summary>
    public partial GpuTexture? DepthImage { set; }
    /// <summary>Stages surfaceImage without transferring ownership.</summary>
    public partial GpuTexture? SurfaceImage { set; }
    /// <summary>Stages depthHalf without transferring ownership.</summary>
    public partial GpuTexture? DepthHalf { set; }
    /// <summary>Stages depthQuarter without transferring ownership.</summary>
    public partial GpuTexture? DepthQuarter { set; }
    /// <summary>Stages depthEighth without transferring ownership.</summary>
    public partial GpuTexture? DepthEighth { set; }
    /// <summary>Stages the complete operation parameters before submission.</summary>
    internal void Capture(VgeFrameUniformBuffer camera, Vector4 frame, Vector4 sampling, Vector4 distance)
    {
        RequireInputMutation();
        frameInputs = camera ?? throw new System.ArgumentNullException(nameof(camera));
        inputs.Capture(frame, sampling, distance);
    }
    /// <summary>Publishes the retained uniform block through the generated binding layout.</summary>
    CpuUniformBuffer IPostSsaoShaderProgramBindings.Inputs => inputs;
    /// <summary>Borrows the common camera snapshot without assigning it to the shader lifetime.</summary>
    CpuUniformBuffer IPostSsaoShaderProgramBindings.FrameInputs => frameInputs ?? throw new System.InvalidOperationException("AO requires a shared camera snapshot.");
    #endregion
}
