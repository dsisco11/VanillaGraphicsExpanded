using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the independent depth-compatible hardware-filtered OIT reconstruction executable.</summary>
[ShaderProgram("Contract","cloud_volume/reconstruct",2)]
[ShaderStage("Contract",ShaderStageKind.Vertex,"cloud_volume/view.vsh")]
[ShaderStage("Contract",ShaderStageKind.Fragment,"cloud_volume/reconstruct.fsh")]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(DiagnosticsEnabled))]
internal sealed partial class CloudVolumeReconstructProgram:GpuProgram,ICloudVolumeReconstructBindings
{
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? lighting;
    private CloudTemporalParameters? temporal;
    /// <summary>Borrows final scene encoding only at native composition.</summary>
    internal GpuShaderStorageBuffer? Lighting {get=>lighting;set{RequireInputMutation();lighting=value;}}
    internal VgeFrameUniformBuffer? Frame {get=>frame;set{RequireInputMutation();frame=value;}}
    /// <summary>Borrows the temporal pass parameters only for the immediate composition.</summary>
    internal CloudTemporalParameters? Temporal {get=>temporal;set{RequireInputMutation();temporal=value;}}
    #region Public API
    /// <summary>Removes diagnostic texture sampling and overlay branches from normal composition.</summary>
    [ShaderOptionReference(typeof(CloudVolumeTemporalOptions),nameof(CloudVolumeTemporalOptions.Diagnostics))]
    internal partial bool DiagnosticsEnabled {get;set;}
    /// <summary>Registers typed reconstruction inputs without duplicating shared frame data.</summary>
    public CloudVolumeReconstructProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    #endregion
    #region Private
    /// <summary>Exposes the generated discovery contract.</summary>
    internal override GpuShaderContract ProgramContract=>Contract;
    /// <summary>Supplies the same immutable frame used by tracing.</summary>
    CpuUniformBuffer ICloudVolumeReconstructBindings.Camera=>Frame??throw new System.InvalidOperationException("Stored cloud reconstruction frame missing.");
    /// <summary>Reuses the filter's diagnostic selection without duplicating its parameter buffer.</summary>
    CpuUniformBuffer ICloudVolumeReconstructBindings.TemporalInputs=>Temporal??throw new System.InvalidOperationException("Stored cloud temporal parameters missing.");
    /// <summary>Encodes accumulated physical radiance with the current frozen frame scale.</summary>
    GpuShaderStorageBuffer CameraExposure.IPhotometricFrameBindings.FrameLighting=>lighting??throw new System.InvalidOperationException("Cloud reconstruction lighting missing.");
    #endregion
}
