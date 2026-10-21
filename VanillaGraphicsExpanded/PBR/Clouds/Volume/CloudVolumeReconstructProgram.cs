using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the independent depth-compatible hardware-filtered OIT reconstruction executable.</summary>
[ShaderProgram("Contract","cloud_volume/reconstruct",1)]
[ShaderStage("Contract",ShaderStageKind.Vertex,"cloud_volume/view.vsh")]
[ShaderStage("Contract",ShaderStageKind.Fragment,"cloud_volume/reconstruct.fsh")]
internal sealed partial class CloudVolumeReconstructProgram:GpuProgram,ICloudVolumeReconstructBindings
{
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? lighting;
    /// <summary>Borrows final scene encoding only at native composition.</summary>
    internal GpuShaderStorageBuffer? Lighting {get=>lighting;set{RequireInputMutation();lighting=value;}}
    internal VgeFrameUniformBuffer? Frame {get=>frame;set{RequireInputMutation();frame=value;}}
    #region Public API
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
    /// <summary>Encodes accumulated physical radiance with the current frozen frame scale.</summary>
    GpuShaderStorageBuffer CameraExposure.IPhotometricFrameBindings.FrameLighting=>lighting??throw new System.InvalidOperationException("Cloud reconstruction lighting missing.");
    #endregion
}
