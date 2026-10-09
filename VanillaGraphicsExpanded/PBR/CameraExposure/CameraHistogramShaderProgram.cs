using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Owns the compiled pbr_camera_histogram executable and its staged inputs.</summary>
[ShaderProgram("Contract", "pbr_camera_histogram", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_camera_exposure.vsh", Identity = "pbr_camera_histogram.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_camera_histogram.fsh")]
internal sealed partial class CameraHistogramShaderProgram : GpuProgram, ICameraHistogramShaderProgramBindings
{
    private readonly CameraExposureInputs inputs;
    #region Public API
    /// <summary>Declares the offline compilation and reload contract.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Creates the retained metering parameter block.</summary>
    public CameraHistogramShaderProgram()
    {
        inputs = OwnUniformBuffer(new CameraExposureInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Stages the SceneRadiance texture for submission.</summary>
    public partial GpuTexture? SceneRadiance { set; }
    /// <summary>Captures the complete metering parameters for one submission.</summary>
    internal void Capture(CameraExposureParameters settings, bool reset)
    {
        inputs.Capture(settings, reset);
    }
    /// <summary>Publishes the parameter block through its shader contract.</summary>
    CpuUniformBuffer ICameraHistogramShaderProgramBindings.Inputs => inputs;
    #endregion
}
