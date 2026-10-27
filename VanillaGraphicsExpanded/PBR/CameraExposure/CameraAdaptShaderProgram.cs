using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Owns the compiled pbr_camera_adapt executable and its staged inputs.</summary>
[ShaderProgram("Contract", "pbr_camera_adapt", 2)]
[ShaderStage("Contract", ShaderStageKind.Compute, "pbr_camera_adapt.csh")]
internal sealed partial class CameraAdaptShaderProgram : GpuComputeProgram, ICameraAdaptShaderProgramBindings
{
    private readonly CameraExposureInputs inputs;
    private VgeFrameUniformBuffer? frameInputs;
    #region Public API
    /// <summary>Declares the typed compute executable through the existing asset and reload owner.</summary>
    internal static CameraAdaptShaderProgram Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(Contract.Stages[0].Bindings);
        return new(ComputeShaderAsset.DeclareFromAssets(api, new ShaderSettings(Contract), layout: layout));
    }
    /// <summary>Creates the retained metering parameter block.</summary>
    public CameraAdaptShaderProgram(ComputeShaderAsset pipeline) : base(pipeline)
    {
        inputs = OwnUniformBuffer(new CameraExposureInputs());
    }
    /// <summary>Stages the Histogram buffer for submission.</summary>
    public partial GpuShaderStorageBuffer? Histogram { set; }
    /// <summary>Stages the PreviousExposure buffer for submission.</summary>
    public partial GpuShaderStorageBuffer? PreviousExposure { set; }
    /// <summary>Receives the next exposure without overwriting previous history.</summary>
    public partial GpuShaderStorageBuffer? OutputExposure { set; }
    /// <summary>Captures the complete metering parameters for one submission.</summary>
    internal void Capture(CameraExposureParameters settings, bool reset, bool localExposureEnabled=false)
    {
        RequireInputMutation();
        inputs.Capture(settings, reset, localExposureEnabled);
    }
    /// <summary>Selects a retained alternate-view snapshot for controlled offscreen submissions.</summary>
    internal VgeFrameUniformBuffer FrameInputs { set { RequireInputMutation(); frameInputs = value; } }
    /// <summary>Reuses the world duration unless a distinct submitting view supplies its snapshot.</summary>
    CpuUniformBuffer ICameraAdaptShaderProgramBindings.FrameInputs => frameInputs ?? VgeFrameRenderer.Current;
    /// <summary>Publishes the parameter block through its shader contract.</summary>
    CpuUniformBuffer ICameraAdaptShaderProgramBindings.Inputs => inputs;
    #endregion
}
