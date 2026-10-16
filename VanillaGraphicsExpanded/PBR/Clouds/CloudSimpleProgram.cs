using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns the native-layout Simple cloud executable and its physical lighting inputs.</summary>
[ShaderProgram("Contract", "cloud_simple", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "cloud_simple.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "cloud_simple.fsh")]
internal sealed partial class CloudSimpleProgram : GpuProgram, ICloudSimpleBindings
{
    internal readonly CloudSimpleParameters Parameters;
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? lighting;

    #region Public API
    /// <summary>Retains the owned effect block and declares both stages' fixed resource contracts.</summary>
    public CloudSimpleProgram()
    {
        Parameters = OwnUniformBuffer(new CloudSimpleParameters());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Identifies the offline compiled cloud program.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Supplies a separately owned camera for controlled or alternate-view submissions.</summary>
    internal VgeFrameUniformBuffer? Frame { get => frame; set { RequireInputMutation(); frame = value; } }
    /// <summary>Supplies the matching frozen storage for a controlled view.</summary>
    internal GpuShaderStorageBuffer? Lighting { get => lighting; set { RequireInputMutation(); lighting = value; } }
    /// <summary>Borrows the current view without advancing the shared camera.</summary>
    CpuUniformBuffer ICloudSimpleBindings.FrameInputs => frame ?? VgeFrameRenderer.Current;
    /// <summary>Publishes the immutable per-submission shape, medium and illumination inputs.</summary>
    CpuUniformBuffer ICloudSimpleBindings.Inputs => Parameters;
    /// <summary>Uses the frozen physical storage publication without changing its scale.</summary>
    GpuShaderStorageBuffer CameraExposure.IPhotometricFrameBindings.FrameLighting => lighting ?? CameraExposure.CameraExposureRenderer.FrozenLighting;
    #endregion
}
