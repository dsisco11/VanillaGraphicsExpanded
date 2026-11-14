using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns the pbr_lightshafts executable and its typed staged inputs.</summary>
[ShaderProgram("Contract", "pbr_lightshafts", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_lightshafts.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_lightshafts.fsh")]
internal sealed partial class LightShaftShaderProgram : GpuProgram, ILightShaftShaderProgramBindings
{
    private readonly PostprocessInputs inputs;
    private VgeFrameUniformBuffer? frameInputs;
    #region Public API
    /// <summary>Declares compilation and reload identity.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Creates the retained parameter block and generated binding layout.</summary>
    public LightShaftShaderProgram() {
        inputs = OwnUniformBuffer(new PostprocessInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Stages the VisibilityImage image.</summary>
    public partial GpuTextureRef VisibilityImage { set; }
    /// <summary>Stages the DepthImage image.</summary>
    public partial GpuTextureRef DepthImage { set; }
    /// <summary>Stages the current scene or radial input.</summary>
    public partial GpuTextureRef SourceImage { set; }
    /// <summary>Stages the published camera exposure buffer.</summary>
    public partial GpuShaderStorageBuffer? ExposureBuffer { set; }
    /// <summary>Stages extraction inputs independently of their uniform representation.</summary>
    internal void CaptureExtraction(bool occlusionOnly, bool automaticExposure, float threshold,
        float limit, float strength, float manualLogGain, LightShaftSource source)
    {
        RequireInputMutation();
        inputs.CaptureShaftExtraction(occlusionOnly, automaticExposure, threshold, limit, strength, manualLogGain, source);
    }
    /// <summary>Stages one radial filtering operation.</summary>
    internal void CaptureFilter(int samples, float step, LightShaftSource source)
    {
        RequireInputMutation();
        inputs.CaptureShaftFilter(samples, step, source);
    }
    /// <summary>Stages the final glare or occlusion publication.</summary>
    internal void CaptureResolve(bool occlusionOnly, LightShaftSource source)
    {
        RequireInputMutation();
        inputs.CaptureShaftResolve(occlusionOnly, source);
    }
    /// <summary>Selects a retained alternate-view snapshot for controlled offscreen submissions.</summary>
    internal VgeFrameUniformBuffer FrameInputs { set { RequireInputMutation(); frameInputs = value; } }
    /// <summary>Shares the world camera unless the submitting owner supplies a distinct view.</summary>
    CpuUniformBuffer ILightShaftShaderProgramBindings.FrameInputs => frameInputs ?? VgeFrameRenderer.Current;
    /// <summary>Publishes the retained input block.</summary>
    CpuUniformBuffer ILightShaftShaderProgramBindings.Inputs => inputs;
    #endregion
}
