using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the complete std140 layout for cloud volume view parameters.</summary>
internal sealed class CloudVolumeViewParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 240;
    internal const int GridOffset = 0;
    internal const int SlotsOffset = 16;
    internal const int OriginOffset = 32;
    internal const int CellSizeOffset = 48;
    internal const int BoundsMinOffset = 64;
    internal const int BoundsMaxOffset = 80;
    internal const int CameraOriginOffset = 96;
    internal const int ControlsOffset = 112;
    internal const int SolarOffset = 128;
    internal const int LunarOffset = 144;
    internal const int EnvironmentOffset = 160;
    internal const int SunDirectionOffset = 176;
    internal const int MoonDirectionOffset = 192;
    internal const int AerialOffset = 208;
    internal const int FogOffset = 224;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages this complete layout from one coherent set of semantic inputs.</summary>
    internal void Capture(CloudVolumeViewFields fields,CloudMapInput input,CloudVolumeViewSettings settings,CloudVolumeViewScene scene,int debugMode)
    {
        settings.Validate();var d=fields.Domain;var light=scene.Atmosphere;
        // Admission reads the same immutable shared fog snapshot that the shader consumes. Only
        // flat-fog geometry and underwater state are absent from that global frame publication.
        var frameFog=scene.Camera.FogParameters;
        var fogColor=new Vector3(frameFog.X,frameFog.Y,frameFog.Z);
        if(!float.IsFinite(scene.Underwater)||!Finite(scene.FlatFog)||!LightingCalibration.FiniteNonnegative(fogColor)||
            !float.IsFinite(light.LookupStorageScale)||light.LookupStorageScale<=0||!float.IsFinite(1/light.LookupStorageScale)||
            !float.IsFinite(light.Altitude)||!float.IsFinite(light.HorizonElevation)||!float.IsFinite(light.ReconstructionMaximumNits)||light.ReconstructionMaximumNits<0)
            throw new ArgumentException("Invalid stored cloud view medium.");
        var bounds=CloudVolumeLightingBounds.Admit(light,fields.LightingBounds,fields.Encoding);
        double peak=Math.Max(fogColor.X,Math.Max(fogColor.Y,fogColor.Z));
        float fog=LightingStorage.UpperBound(32*(peak<=.04045?peak/12.92:Math.Pow((peak+.055)/1.055,2.4)));
        _=CloudVolumeAdmission.Maximum(bounds,light.ReconstructionMaximumNits,fog);
        Span<byte> data=stackalloc byte[BlockSize];data.Clear();
        UboPacking.WriteIVec4(data,GridOffset,d.Size,CloudVolumeDomain.Height,d.Size,0);
        UboPacking.WriteIVec4(data,SlotsOffset,d.SlotX,d.SlotZ,d.BlocksX,d.BlocksY);
        UboPacking.WriteVec4(data,OriginOffset,(float)(d.FirstX*CloudVolumeDomain.HorizontalCell-d.AnchorX),(float)(d.Bottom-d.VerticalCell),(float)(d.FirstZ*CloudVolumeDomain.HorizontalCell-d.AnchorZ),0);
        UboPacking.WriteVec4(data,CellSizeOffset,(float)CloudVolumeDomain.HorizontalCell,d.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,0);
        UboPacking.WriteVec4(data,BoundsMinOffset,0,(float)d.Bottom,0,0);UboPacking.WriteVec4(data,BoundsMaxOffset,50*d.Width,(float)(d.Bottom+d.Band),50*d.Width,0);
        var origin=CloudVolumeCoordinates.Origin(d,input);UboPacking.WriteVec4(data,CameraOriginOffset,origin.X,origin.Y,origin.Z,0);
        UboPacking.WriteVec4(data,ControlsOffset,settings.Stop,fields.Encoding.Scale,0,debugMode);
        UboPacking.WriteVec4(data,SolarOffset,light.Solar.X,light.Solar.Y,light.Solar.Z,0);
        UboPacking.WriteVec4(data,LunarOffset,light.Lunar.X,light.Lunar.Y,light.Lunar.Z,0);
        float debugPeak=Math.Max(bounds.Encoding.Cap.X,Math.Max(bounds.Encoding.Cap.Y,bounds.Encoding.Cap.Z));
        UboPacking.WriteVec4(data,EnvironmentOffset,light.Environment.X,light.Environment.Y,light.Environment.Z,debugPeak);
        Vector3 sun=Direction(light.Sun),moon=Direction(light.Moon);
        UboPacking.WriteVec4(data,SunDirectionOffset,sun.X,sun.Y,sun.Z,0);UboPacking.WriteVec4(data,MoonDirectionOffset,moon.X,moon.Y,moon.Z,0);
        UboPacking.WriteVec4(data,AerialOffset,light.Altitude,light.HorizonElevation,scene.Underwater,1/light.LookupStorageScale);
        UboPacking.WriteVec4(data,FogOffset,scene.FlatFog.X,scene.FlatFog.Y,0,0);SetBytes(data);
    }
    #endregion

    #region Private
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
