using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Selects one immutable storage scale before any physical-light draw, independently of subsequent metering.</summary>
[ShaderProgram("Contract", "pbr_frame_lighting", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "pbr_frame_lighting.csh")]
internal sealed partial class FrameLightingShaderProgram : GpuComputeProgram, IFrameLightingShaderBindings
{
    private readonly FrameLightingInputs inputs;
    #region Public API
    /// <summary>Declares the compute executable under the existing shader library and binding owners.</summary>
    internal static FrameLightingShaderProgram Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(Contract.Stages[0].Bindings);
        return new(GpuComputePipeline.DeclareFromAssets(api, new ShaderSettings(Contract), layout: layout));
    }
    /// <summary>Owns retained input packing for repeated allocation-free submissions.</summary>
    public FrameLightingShaderProgram(GpuComputePipeline pipeline) : base(pipeline)
    {
        inputs = OwnUniformBuffer(new FrameLightingInputs());
    }
    /// <summary>Stages the optional previous automatic EV publication.</summary>
    public partial GpuShaderStorageBuffer? PreviousExposure { set; }
    /// <summary>Stages the disjoint frozen frame-scale buffer.</summary>
    public partial GpuShaderStorageBuffer? Output { set; }
    /// <summary>Captures matching source metadata; no producer may overwrite it after freezing.</summary>
    internal void Capture(float physicalBound, float manualMultiplier, bool automaticHistory, uint frame, uint calibrationGeneration)
    {
        RequireInputMutation();
        inputs.Capture(physicalBound, manualMultiplier, automaticHistory, frame, calibrationGeneration);
    }
    /// <summary>Supplies the retained selection inputs through generated bindings.</summary>
    CpuUniformBuffer IFrameLightingShaderBindings.Inputs => inputs;
    #endregion
}
