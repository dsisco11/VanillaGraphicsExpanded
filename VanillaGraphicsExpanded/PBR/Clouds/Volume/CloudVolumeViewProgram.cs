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
    private readonly PackedUniformBuffer view;
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
        view=OwnUniformBuffer(new PackedUniformBuffer(240));
        Temporal=OwnUniformBuffer(new CloudTemporalParameters());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Captures the published geometry/scale independently of native procedural controls.</summary>
    internal void Capture(CloudVolumeViewFields fields,CloudMapInput input,CloudVolumeViewSettings settings,CloudVolumeViewScene scene,int debugMode)
    {
        RequireInputMutation();settings.Validate();var d=fields.Domain;var light=scene.Atmosphere;
        // Admission reads the same immutable shared fog snapshot that the shader consumes. Only
        // flat-fog geometry and underwater state are absent from that global frame publication.
        var frameFog=MemoryMarshal.Read<Vector4>(scene.Camera.Bytes.Slice(416,16));
        var fogColor=new Vector3(frameFog.X,frameFog.Y,frameFog.Z);
        if(!float.IsFinite(scene.Underwater)||!Finite(scene.FlatFog)||!LightingCalibration.FiniteNonnegative(fogColor)||
            !float.IsFinite(light.LookupStorageScale)||light.LookupStorageScale<=0||!float.IsFinite(1/light.LookupStorageScale)||
            !float.IsFinite(light.Altitude)||!float.IsFinite(light.HorizonElevation)||!float.IsFinite(light.ReconstructionMaximumNits)||light.ReconstructionMaximumNits<0)
            throw new ArgumentException("Invalid stored cloud view medium.");
        var bounds=CloudVolumeLightingBounds.Admit(light,fields.LightingBounds,fields.Encoding);
        double peak=Math.Max(fogColor.X,Math.Max(fogColor.Y,fogColor.Z));
        float fog=LightingStorage.UpperBound(32*(peak<=.04045?peak/12.92:Math.Pow((peak+.055)/1.055,2.4)));
        _=CloudVolumeAdmission.Maximum(bounds,light.ReconstructionMaximumNits,fog);
        Span<byte> data=stackalloc byte[240];data.Clear();
        UboPacking.WriteIVec4(data,0,d.Size,CloudVolumeDomain.Height,d.Size,0);
        UboPacking.WriteIVec4(data,16,d.SlotX,d.SlotZ,d.BlocksX,d.BlocksY);
        UboPacking.WriteVec4(data,32,(float)(d.FirstX*CloudVolumeDomain.HorizontalCell-d.AnchorX),(float)(d.Bottom-d.VerticalCell),(float)(d.FirstZ*CloudVolumeDomain.HorizontalCell-d.AnchorZ),0);
        UboPacking.WriteVec4(data,48,(float)CloudVolumeDomain.HorizontalCell,d.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,0);
        UboPacking.WriteVec4(data,64,0,(float)d.Bottom,0,0);UboPacking.WriteVec4(data,80,50*d.Width,(float)(d.Bottom+d.Band),50*d.Width,0);
        var origin=CloudVolumeCoordinates.Origin(d,input);UboPacking.WriteVec4(data,96,origin.X,origin.Y,origin.Z,0);
        UboPacking.WriteVec4(data,112,settings.Stop,fields.Encoding.Scale,0,debugMode);
        UboPacking.WriteVec4(data,128,light.Solar.X,light.Solar.Y,light.Solar.Z,0);
        UboPacking.WriteVec4(data,144,light.Lunar.X,light.Lunar.Y,light.Lunar.Z,0);
        float debugPeak=Math.Max(bounds.Encoding.Cap.X,Math.Max(bounds.Encoding.Cap.Y,bounds.Encoding.Cap.Z));
        UboPacking.WriteVec4(data,160,light.Environment.X,light.Environment.Y,light.Environment.Z,debugPeak);
        Vector3 sun=Direction(light.Sun),moon=Direction(light.Moon);
        UboPacking.WriteVec4(data,176,sun.X,sun.Y,sun.Z,0);UboPacking.WriteVec4(data,192,moon.X,moon.Y,moon.Z,0);
        UboPacking.WriteVec4(data,208,light.Altitude,light.HorizonElevation,scene.Underwater,1/light.LookupStorageScale);
        UboPacking.WriteVec4(data,224,scene.FlatFog.X,scene.FlatFog.Y,0,0);view.SetBytes(data);
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
    /// <summary>Checks every medium component before GPU upload.</summary>
    private static bool Finite(Vector2 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y);
    /// <summary>Normalizes finite source directions without overflow.</summary>
    private static Vector3 Direction(Vector3 value)
    {
        double length=Math.Sqrt((double)value.X*value.X+(double)value.Y*value.Y+(double)value.Z*value.Z);
        if(!double.IsFinite(length)||length<=0)throw new ArgumentException("Invalid cloud source direction.");
        return new((float)(value.X/length),(float)(value.Y/length),(float)(value.Z/length));
    }
    #endregion
}
