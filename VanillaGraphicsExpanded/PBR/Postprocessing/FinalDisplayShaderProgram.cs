using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns final HDR composition, display conversion and native screen effects.</summary>
[ShaderProgram("Contract", "pbr_final", 4)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_final.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_final.fsh")]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(LocalExposureEnabled))]
internal sealed partial class FinalDisplayShaderProgram : GpuProgram, IFinalDisplayShaderProgramBindings
{
    private readonly FinalDisplayInputs inputs;
    private VgeFrameUniformBuffer? frameInputs;
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
    /// <summary>Borrows the complete display lookup matching the captured grading controls.</summary>
    public partial DynamicTexture3D? GradingLookup { set; }
    /// <summary>Stages the published exposure buffer.</summary>
    public partial GpuShaderStorageBuffer? ExposureBuffer { set; }
    /// <summary>Selects the optional local variant without increasing disabled-path resource requirements.</summary>
    [ShaderOption("VGE_LOCAL_EXPOSURE",false)]
    internal partial bool LocalExposureEnabled { get; set; }
    /// <summary>Borrows the completed current-frame log-luminance base.</summary>
    public partial GpuTexture? LocalBase { set; }
    /// <summary>Captures local policy separately from cached color-grading controls.</summary>
    internal void CaptureLocal(LocalExposure.LocalExposureParameters settings)=>inputs.CaptureLocal(settings);
    /// <summary>Borrows a camera/frame snapshot for alternate views and fixtures.</summary>
    internal VgeFrameUniformBuffer? FrameInputs
    {
        get => frameInputs;
        set { RequireInputMutation(); frameInputs = value; }
    }
    /// <summary>Stages native grading, antialiasing and published camera exposure.</summary>
    internal void Capture(bool antialias,FinalDisplayParameters display,Vector4 exposure)=>inputs.Capture(antialias,display,exposure);
    /// <summary>Supplies the generated block binding.</summary>
    CpuUniformBuffer IFinalDisplayShaderProgramBindings.Inputs=>inputs;
    /// <summary>Reuses the existing shared view publication for full-resolution pixel spacing.</summary>
    CpuUniformBuffer IFinalDisplayShaderProgramBindings.FrameInputs=>frameInputs ?? VgeFrameRenderer.Current;
    #endregion
}
