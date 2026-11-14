using System;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns reduced-resolution stored-density view variants and their frozen physical scene inputs.</summary>
[ShaderProgram("Contract","cloud_volume/view",24)]
[ShaderStage("Contract",ShaderStageKind.Vertex,"cloud_volume/view.vsh")]
[ShaderStage("Contract",ShaderStageKind.Fragment,"cloud_volume/view.fsh")]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(SelfShadowing))]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(Diffuse))]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(Termination))]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(TraceDivisor))]
internal sealed partial class CloudVolumeViewProgram:GpuProgram,ICloudVolumeViewBindings
{
    private readonly CloudVolumeViewParameters view;
    internal readonly CloudTemporalParameters Temporal;
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? lighting;
    internal VgeFrameUniformBuffer? Frame {get=>frame;set{RequireInputMutation();frame=value;}}
    internal GpuShaderStorageBuffer? Lighting {get=>lighting;set{RequireInputMutation();lighting=value;}}
    #region Public API
    /// <summary>Compiles the selected full, half or quarter trace and output contract.</summary>
    [ShaderOptionReference(typeof(CloudVolumeViewOptions),nameof(CloudVolumeViewOptions.TraceDivisor))]
    internal partial int TraceDivisor {get;set;}
    /// <summary>Removes the direct sampler when disabled.</summary>
    [ShaderOptionReference(typeof(CloudVolumeViewOptions),nameof(CloudVolumeViewOptions.Shadows))]
    internal partial bool SelfShadowing {get;set;}
    /// <summary>Removes the diffuse sampler when disabled.</summary>
    [ShaderOptionReference(typeof(CloudVolumeViewOptions),nameof(CloudVolumeViewOptions.Diffuse))]
    internal partial bool Diffuse {get;set;}
    /// <summary>Removes the optical exit when zero is configured.</summary>
    [ShaderOptionReference(typeof(CloudVolumeViewOptions),nameof(CloudVolumeViewOptions.Termination))]
    internal partial bool Termination {get;set;}
    /// <summary>Owns one parameter record across compiled variants and registers ordinary typed view contracts.</summary>
    public CloudVolumeViewProgram()
    {
        view=OwnUniformBuffer(new CloudVolumeViewParameters());
        Temporal=OwnUniformBuffer(new CloudTemporalParameters());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Captures the published geometry/scale independently of native procedural controls.</summary>
    internal void Capture(CloudVolumeViewFields fields,CloudMapInput input,CloudVolumeViewSettings settings,CloudVolumeViewScene scene,int debugMode)
    {
        RequireInputMutation();
        view.Capture(fields, input, settings, scene, debugMode);
    }
    #endregion
    #region Private
    /// <summary>Uses the independently declared shader discovery contract.</summary>
    internal override GpuShaderContract ProgramContract=>Contract;
    /// <summary>Publishes the current immutable camera.</summary>
    CpuUniformBuffer ICloudVolumeViewBindings.Camera=>Frame??throw new InvalidOperationException("Stored cloud camera missing.");
    /// <summary>Publishes finite geometry and medium parameters.</summary>
    CpuUniformBuffer ICloudVolumeViewBindings.View=>view;
    /// <summary>Publishes common cloud history admission and sampling phase.</summary>
    CpuUniformBuffer ICloudVolumeViewBindings.TemporalInputs=>Temporal;
    /// <summary>Publishes the independent frozen photometric calibration.</summary>
    GpuShaderStorageBuffer IPhotometricFrameBindings.FrameLighting=>Lighting??throw new InvalidOperationException("Stored cloud scene lighting missing.");
    #endregion
}
