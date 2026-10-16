using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns the cloud reconstruction executable and its immutable submission parameters.</summary>
[ShaderProgram("Contract", "cloud_volume_trace", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "cloud_volume.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "cloud_volume_trace.fsh")]
internal sealed partial class CloudTraceProgram : GpuProgram, ICloudTraceBindings
{
    internal readonly CloudVolumeParameters Parameters;
    internal readonly CloudTemporalParameters Temporal;
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? lighting;

    #region Public API
    /// <summary>Registers the dedicated resource contract and owns its parameter block.</summary>
    public CloudTraceProgram()
    {
        Parameters = OwnUniformBuffer(new CloudVolumeParameters());
        Temporal = OwnUniformBuffer(new CloudTemporalParameters());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Identifies the offline volume stages.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Accepts a complete alternate camera rather than implicitly reusing main-view transforms.</summary>
    internal VgeFrameUniformBuffer? Frame { get => frame; set { RequireInputMutation(); frame = value; } }
    /// <summary>Accepts matching controlled-view frozen lighting storage.</summary>
    internal GpuShaderStorageBuffer? Lighting { get => lighting; set { RequireInputMutation(); lighting = value; } }
    /// <summary>Uses the current camera without changing it.</summary>
    CpuUniformBuffer ICloudVolumeBindings.FrameInputs => frame ?? VgeFrameRenderer.Current;
    /// <summary>Publishes the owned submission block.</summary>
    CpuUniformBuffer ICloudVolumeBindings.Inputs => Parameters;
    /// <summary>Encodes against the frozen view publication.</summary>
    GpuShaderStorageBuffer CameraExposure.IPhotometricFrameBindings.FrameLighting => lighting ?? CameraExposure.CameraExposureRenderer.FrozenLighting;
    /// <summary>Publishes owned reprojection parameters.</summary>
    CpuUniformBuffer ICloudTraceBindings.TemporalInputs => Temporal;
    #endregion
}
