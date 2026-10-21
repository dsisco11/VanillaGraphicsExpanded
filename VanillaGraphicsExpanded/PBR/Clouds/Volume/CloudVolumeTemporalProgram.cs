using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Filters compact physical view samples using the shared cloud reprojection certificate.</summary>
[ShaderProgram("Contract","cloud_volume/temporal",1)]
[ShaderStage("Contract",ShaderStageKind.Vertex,"cloud_volume/view.vsh")]
[ShaderStage("Contract",ShaderStageKind.Fragment,"cloud_volume/temporal.fsh")]
internal sealed partial class CloudVolumeTemporalProgram:GpuProgram,ICloudVolumeTemporalBindings
{
    internal readonly CloudTemporalParameters Temporal;
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? lighting;
    /// <summary>Borrows the same admitted photometric frame as the current view.</summary>
    internal GpuShaderStorageBuffer? Lighting {get=>lighting;set{RequireInputMutation();lighting=value;}}
    #region Public API
    /// <summary>Registers the compact payload adapter and owns only its local reprojection parameters.</summary>
    public CloudVolumeTemporalProgram()
    {
        Temporal=OwnUniformBuffer(new CloudTemporalParameters());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Borrows the immutable camera shared by tracing and native composition.</summary>
    internal VgeFrameUniformBuffer? Frame {get=>frame;set{RequireInputMutation();frame=value;}}
    #endregion
    #region Private
    /// <summary>Exposes the generated offline discovery contract.</summary>
    internal override GpuShaderContract ProgramContract=>Contract;
    /// <summary>Supplies the current full-view camera without a duplicate global UBO.</summary>
    CpuUniformBuffer ICloudVolumeReconstructBindings.Camera=>frame??throw new InvalidOperationException("Cloud temporal camera missing.");
    /// <summary>Supplies the shared reprojection layout.</summary>
    CpuUniformBuffer ICloudVolumeTemporalBindings.TemporalInputs=>Temporal;
    /// <summary>Supplies the current frame certificate without retaining transient encoded history.</summary>
    GpuShaderStorageBuffer CameraExposure.IPhotometricFrameBindings.FrameLighting=>lighting??throw new InvalidOperationException("Cloud temporal lighting missing.");
    #endregion
}
