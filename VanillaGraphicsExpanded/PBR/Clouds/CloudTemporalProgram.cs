using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns depth-tested temporal reconstruction of every native OIT payload channel.</summary>
[ShaderProgram("Contract","cloud_volume_temporal",1)]
[ShaderStage("Contract",ShaderStageKind.Vertex,"cloud_volume.vsh")]
[ShaderStage("Contract",ShaderStageKind.Fragment,"cloud_volume_temporal.fsh")]
internal sealed partial class CloudTemporalProgram : GpuProgram,ICloudTemporalBindings
{
    internal readonly CloudTemporalParameters Temporal;
    private VgeFrameUniformBuffer? frame;
    #region Public API
    /// <summary>Owns its exact parameter block and registers both offline shader stages.</summary>
    public CloudTemporalProgram()
    {
        Temporal=OwnUniformBuffer(new CloudTemporalParameters());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings); ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Identifies the independently owned reconstruction executable.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Accepts a complete controlled camera for qualification.</summary>
    internal VgeFrameUniformBuffer? Frame { get=>frame; set { RequireInputMutation(); frame=value; } }
    /// <summary>Supplies the current main-view camera without changing it.</summary>
    CpuUniformBuffer ICloudTemporalBindings.FrameInputs => frame??VgeFrameRenderer.Current;
    /// <summary>Supplies owned reprojection parameters.</summary>
    CpuUniformBuffer ICloudTemporalBindings.TemporalInputs => Temporal;
    #endregion
}
