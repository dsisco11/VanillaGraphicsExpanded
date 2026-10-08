using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns final HDR composition, display conversion and native screen effects.</summary>
[ShaderProgram("Contract", "pbr_final", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_final.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_final.fsh")]
internal sealed partial class FinalDisplayShaderProgram : GpuProgram, IFinalDisplayShaderProgramBindings
{
    private readonly FinalDisplayInputs inputs;
    #region Public API
    /// <summary>Declares the owned executable identity.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Registers typed resources and retained uniform storage.</summary>
    public FinalDisplayShaderProgram()
    {
        inputs=OwnUniformBuffer(new FinalDisplayInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Stages the SceneImage sampler.</summary>
    public partial GpuTexture? SceneImage { set; }
    /// <summary>Stages the BloomImage sampler.</summary>
    public partial GpuTexture? BloomImage { set; }
    /// <summary>Stages the ShaftImage sampler.</summary>
    public partial GpuTexture? ShaftImage { set; }
    /// <summary>Stages the OcclusionImage sampler.</summary>
    public partial GpuTexture? OcclusionImage { set; }
    /// <summary>Stages the ExposureImage sampler.</summary>
    public partial GpuTexture? ExposureImage { set; }
    /// <summary>Stages frame dimensions, native grading and published camera exposure.</summary>
    internal void Capture(Vector4 frame,FinalDisplayParameters display,Vector4 exposure)=>inputs.Capture(frame,display,exposure);
    /// <summary>Supplies the generated block binding.</summary>
    CpuUniformBuffer IFinalDisplayShaderProgramBindings.Inputs=>inputs;
    #endregion
}
