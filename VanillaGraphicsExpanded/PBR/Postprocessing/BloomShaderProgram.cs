using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns the pbr_bloom executable and its typed staged inputs.</summary>
[ShaderProgram("Contract", "pbr_bloom", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_bloom.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_bloom.fsh")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(LocalExposureEnabled))]
internal sealed partial class BloomShaderProgram : GpuProgram, IBloomShaderProgramBindings
{
    private readonly BloomInputs inputs;
    #region Public API
    /// <summary>Declares compilation and reload identity.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Creates the retained parameter block and generated binding layout.</summary>
    public BloomShaderProgram() {
        inputs = OwnUniformBuffer(new BloomInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Stages the SourceImage image.</summary>
    public partial GpuTextureRef SourceImage { set; }
    /// <summary>Stages the SecondaryImage image.</summary>
    public partial GpuTextureRef SecondaryImage { set; }
    /// <summary>Selects source-local exposure without requiring its sampler on the disabled path.</summary>
    [ShaderOption("VGE_LOCAL_EXPOSURE", false)]
    internal partial bool LocalExposureEnabled { get; set; }
    /// <summary>Borrows the completed current-frame scene base for highlight extraction.</summary>
    public partial GpuTextureRef LocalBase { set; }
    /// <summary>Stages the bounded source-local correction independently of per-pass filtering inputs.</summary>
    internal void CaptureLocal(LocalExposure.LocalExposureParameters settings, float logMiddleGray)
        => inputs.CaptureLocal(settings, logMiddleGray);
    /// <summary>Stages the published exposure buffer.</summary>
    public partial GpuShaderStorageBuffer? ExposureBuffer { set; }
    /// <summary>Stages extraction or reduction with named exposure and threshold inputs.</summary>
    internal void CaptureExtraction(bool extract, bool automaticExposure, float threshold, float knee, float strength, float manualLogGain)
    {
        RequireInputMutation();
        inputs.CaptureExtraction(extract, automaticExposure, threshold, knee, strength, manualLogGain);
    }
    /// <summary>Stages a paired Gaussian with source-sized sampling.</summary>
    internal void CaptureGaussian(int radius, bool vertical)
    {
        RequireInputMutation();
        inputs.CaptureGaussian(radius, vertical);
    }
    /// <summary>Stages a weighted scale and optional coarser contribution.</summary>
    internal void CaptureAccumulation(Vector3 tint, bool hasCoarser)
    {
        RequireInputMutation();
        inputs.CaptureAccumulation(tint, hasCoarser);
    }
    /// <summary>Publishes the retained input block.</summary>
    CpuUniformBuffer IBloomShaderProgramBindings.Inputs => inputs;
    #endregion

}
