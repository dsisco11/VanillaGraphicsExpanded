using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Owns the compiled pbr_camera_histogram executable and its staged inputs.</summary>
[ShaderProgram("Contract", "pbr_camera_histogram", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "pbr_camera_histogram.csh")]
internal sealed partial class CameraHistogramShaderProgram : GpuComputeProgram, ICameraHistogramShaderProgramBindings
{
    private readonly CameraExposureInputs inputs;
    #region Public API
    /// <summary>Declares the typed compute executable through the existing asset and reload owner.</summary>
    internal static CameraHistogramShaderProgram Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(Contract.Stages[0].Bindings);
        return new(GpuComputePipeline.DeclareFromAssets(api, new ShaderSettings(Contract), layout: layout));
    }
    /// <summary>Creates the retained metering parameter block.</summary>
    public CameraHistogramShaderProgram(GpuComputePipeline pipeline) : base(pipeline)
    {
        inputs = OwnUniformBuffer(new CameraExposureInputs());
    }
    /// <summary>Stages the SceneRadiance texture for submission.</summary>
    public partial GpuTexture? SceneRadiance { set; }
    /// <summary>Receives disjoint workgroup histograms.</summary>
    public partial GpuShaderStorageBuffer? Histogram { set; }
    /// <summary>Captures the complete metering parameters for one submission.</summary>
    internal void Capture(CameraExposureParameters settings, bool reset, bool localExposureEnabled=false)
    {
        RequireInputMutation();
        inputs.Capture(settings, reset, localExposureEnabled);
    }
    /// <summary>Publishes the parameter block through its shader contract.</summary>
    CpuUniformBuffer ICameraHistogramShaderProgramBindings.Inputs => inputs;
    #endregion
}
