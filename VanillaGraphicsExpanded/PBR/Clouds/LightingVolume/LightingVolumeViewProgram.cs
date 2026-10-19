using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Owns deterministic full-resolution view variants independently of the existing cloud transport graph.</summary>
[ShaderProgram("Contract","cloud_lighting_volume/view",16)]
[ShaderStage("Contract",ShaderStageKind.Vertex,"cloud_lighting_volume/view.vsh")]
[ShaderStage("Contract",ShaderStageKind.Fragment,"cloud_lighting_volume/view.fsh")]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(Detail))]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(SelfShadowing))]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(Diffuse))]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(Termination))]
internal sealed partial class LightingVolumeViewProgram:GpuProgram,ILightingVolumeViewBindings
{
    private readonly LightingVolumeParameters grid;
    private readonly PackedUniformBuffer view;
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? lighting;
    internal VgeFrameUniformBuffer? Frame { get=>frame;set {RequireInputMutation();frame=value;} }
    internal GpuShaderStorageBuffer? Lighting { get=>lighting;set {RequireInputMutation();lighting=value;} }
    #region Public API
    /// <summary>Selects the compiled private density detail path.</summary>
    [ShaderOptionReference(typeof(LightingVolumeShaderOptions),nameof(LightingVolumeShaderOptions.Detail))]
    internal partial bool Detail { get; set; }
    /// <summary>Selects the compiled direct-transmission lookup path.</summary>
    [ShaderOptionReference(typeof(LightingVolumeShaderOptions),nameof(LightingVolumeShaderOptions.Direct))]
    internal partial bool SelfShadowing { get; set; }
    /// <summary>Selects the compiled progressive diffuse lookup path.</summary>
    [ShaderOptionReference(typeof(LightingVolumeShaderOptions),nameof(LightingVolumeShaderOptions.Diffuse))]
    internal partial bool Diffuse { get; set; }
    /// <summary>Selects the compiled post-step termination path.</summary>
    [ShaderOptionReference(typeof(LightingVolumeShaderOptions),nameof(LightingVolumeShaderOptions.Termination))]
    internal partial bool Termination { get; set; }
    /// <summary>Owns two small parameter records and registers typed vertex/fragment contracts.</summary>
    public LightingVolumeViewProgram()
    {
        grid=OwnUniformBuffer(new LightingVolumeParameters());view=OwnUniformBuffer(new PackedUniformBuffer(144));
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Stages one bounded current native/grid transform, physical source snapshot and medium.</summary>
    internal void Capture(LightingVolumeDomain domain,CloudPlacement placement,LightingVolumeEncoding encoding,
        LightingVolumeViewSettings settings,LightingVolumeViewScene scene)
    {
        RequireInputMutation();settings.Validate();var light=scene.Atmosphere;
        // Physical source admission is independent of exposure and never calls the existing cloud transport owner.
        if(!encoding.IsValid||!LightingCalibration.FiniteNonnegative(light.Solar)||!LightingCalibration.FiniteNonnegative(light.Lunar)||
            !LightingCalibration.FiniteNonnegative(light.Environment)||!float.IsFinite(light.ReconstructionMaximumNits)||
            light.ReconstructionMaximumNits<0) throw new ArgumentException("Invalid physical cloud source.");
        // Validate the readable field's retained cap as well as current fallback/source arithmetic.
        double peakSolar=Math.Max(light.Solar.X,Math.Max(light.Solar.Y,light.Solar.Z));
        double peakLunar=Math.Max(light.Lunar.X,Math.Max(light.Lunar.Y,light.Lunar.Z));
        double peakEnvironment=Math.Max(light.Environment.X,Math.Max(light.Environment.Y,light.Environment.Z));
        double q=.9*1.6/(4*Math.PI*.4*.4)*(peakSolar+peakLunar);
        if(!float.IsFinite(scene.Underwater)||!Finite(scene.Fog)||scene.Fog.X<0 ||
            !LightingCalibration.FiniteNonnegative(scene.FogColor)||!float.IsFinite(light.LookupStorageScale)||light.LookupStorageScale<=0 ||
            !float.IsFinite(1/light.LookupStorageScale)||!float.IsFinite(light.Altitude)||!float.IsFinite(light.HorizonElevation))
            throw new ArgumentException("Invalid lighting-volume view medium.");
        double fogPeak=Math.Max(scene.FogColor.X,Math.Max(scene.FogColor.Y,scene.FogColor.Z));
        double fogMaximum=32*(fogPeak<=.04045?fogPeak/12.92:Math.Pow((fogPeak+.055)/1.055,2.4));
        _=LightingStorage.UpperBound((q+Math.Max(encoding.Cap,.9*peakEnvironment)+light.ReconstructionMaximumNits+fogMaximum)*
            (1+16384.0/(1<<23)));
        grid.Capture(domain,placement,encoding);Span<byte> bytes=stackalloc byte[144];
        Vector3 sun=Direction(light.Sun),moon=Direction(light.Moon);
        UboPacking.WriteVec4(bytes,0,settings.Maximum,settings.BudgetDistance,settings.Stop,0);
        UboPacking.WriteVec4(bytes,16,light.Solar.X,light.Solar.Y,light.Solar.Z,0);
        UboPacking.WriteVec4(bytes,32,light.Lunar.X,light.Lunar.Y,light.Lunar.Z,0);
        UboPacking.WriteVec4(bytes,48,light.Environment.X,light.Environment.Y,light.Environment.Z,0);
        UboPacking.WriteVec4(bytes,64,sun.X,sun.Y,sun.Z,0);UboPacking.WriteVec4(bytes,80,moon.X,moon.Y,moon.Z,0);
        UboPacking.WriteVec4(bytes,96,light.Altitude,light.HorizonElevation,scene.Underwater,1/light.LookupStorageScale);
        UboPacking.WriteVec4(bytes,112,scene.Fog.X,scene.Fog.Y,scene.Fog.Z,scene.Fog.W);
        UboPacking.WriteVec4(bytes,128,scene.FogColor.X,scene.FogColor.Y,scene.FogColor.Z,0);view.SetBytes(bytes);
    }
    #endregion
    #region Private
    #region Binding publication
    /// <summary>Uses this independent offline declaration for executable discovery.</summary>
    internal override GpuShaderContract ProgramContract=>Contract;
    /// <summary>Supplies the complete captured camera.</summary>
    CpuUniformBuffer ILightingVolumeViewBindings.Camera=>Frame??throw new InvalidOperationException("Candidate camera missing.");
    /// <summary>Supplies bounded grid coordinates and the published decode scale.</summary>
    CpuUniformBuffer ILightingVolumeViewBindings.Grid=>grid;
    /// <summary>Supplies physical sources and the current finite view schedule.</summary>
    CpuUniformBuffer ILightingVolumeViewBindings.View=>view;
    /// <summary>Uses the explicit matching frozen scene storage publication.</summary>
    GpuShaderStorageBuffer IPhotometricFrameBindings.FrameLighting=>Lighting??throw new InvalidOperationException("Candidate scene storage missing.");
    #endregion
    #region Physical input admission
    /// <summary>Checks local medium values before staging any publication.</summary>
    private static bool Finite(Vector4 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z)&&float.IsFinite(v.W);
    /// <summary>Normalizes finite celestial directions in double precision without overflow.</summary>
    private static Vector3 Direction(Vector3 v)
    {
        double length=Math.Sqrt((double)v.X*v.X+(double)v.Y*v.Y+(double)v.Z*v.Z);
        if(!double.IsFinite(length)||length<=0) throw new ArgumentException("Invalid candidate source direction.");
        return new((float)(v.X/length),(float)(v.Y/length),(float)(v.Z/length));
    }
    #endregion
    #endregion
}
