using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Owns the compiled pbr_camera_adapt executable and its staged inputs.</summary>
[ShaderProgram("Contract", "pbr_camera_adapt", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_camera_exposure.vsh", Identity = "pbr_camera_adapt.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_camera_adapt.fsh")]
internal sealed partial class CameraAdaptShaderProgram : GpuProgram, ICameraAdaptShaderProgramBindings
{
    private readonly CameraExposureInputs inputs;
    #region Public API
    /// <summary>Declares the offline compilation and reload contract.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Creates the retained metering parameter block.</summary>
    public CameraAdaptShaderProgram()
    {
        inputs = OwnUniformBuffer(new CameraExposureInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Stages the Histogram texture for submission.</summary>
    public partial GpuTexture? Histogram { set; }
    /// <summary>Stages the PreviousExposure texture for submission.</summary>
    public partial GpuTexture? PreviousExposure { set; }
    /// <summary>Captures the complete metering parameters for one submission.</summary>
    internal void Capture(CameraExposureParameters settings, float deltaTime, bool reset)
    {
        inputs.Capture(settings, deltaTime, reset);
    }
    /// <summary>Publishes the parameter block through its shader contract.</summary>
    CpuUniformBuffer ICameraAdaptShaderProgramBindings.Inputs => inputs;
    #endregion
}
