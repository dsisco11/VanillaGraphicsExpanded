using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the complete std140 layout for cloud volume diffuse parameters.</summary>
internal sealed class CloudVolumeDiffuseParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 208;
    internal const int GridOffset = 0;
    internal const int SlotsOffset = 16;
    internal const int OriginOffset = 32;
    internal const int CellSizeOffset = 48;
    internal const int BoundsMinOffset = 64;
    internal const int BoundsMaxOffset = 80;
    internal const int SunDirectionOffset = 96;
    internal const int MoonDirectionOffset = 112;
    internal const int SolarOffset = 128;
    internal const int LunarOffset = 144;
    internal const int EnvironmentOffset = 160;
    internal const int EncodingCapOffset = 176;
    internal const int EncodingScaleOffset = 192;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages this complete layout from one coherent set of semantic inputs.</summary>
    internal void Capture(CloudVolumeWorkBatch batch,CloudVolumeEncoding target)
    {
        var d=batch.Fields.Domain;var light=batch.Lighting;
        Span<byte> data=stackalloc byte[BlockSize];data.Clear();
        UboPacking.WriteIVec4(data,GridOffset,d.Size,CloudVolumeDomain.Height,d.Size,0);
        UboPacking.WriteIVec4(data,SlotsOffset,d.SlotX,d.SlotZ,d.BlocksX,d.BlocksY);
        UboPacking.WriteVec4(data,OriginOffset,(float)(d.FirstX*CloudVolumeDomain.HorizontalCell-d.AnchorX),(float)(d.Bottom-d.VerticalCell),
            (float)(d.FirstZ*CloudVolumeDomain.HorizontalCell-d.AnchorZ),0);
        UboPacking.WriteVec4(data,CellSizeOffset,(float)CloudVolumeDomain.HorizontalCell,d.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,0);
        UboPacking.WriteVec4(data,BoundsMinOffset,0,(float)d.Bottom,0,0);
        UboPacking.WriteVec4(data,BoundsMaxOffset,50*d.Width,(float)(d.Bottom+d.Band),50*d.Width,0);
        UboPacking.WriteVec4(data,SunDirectionOffset,light.Sun.X,light.Sun.Y,light.Sun.Z,0);
        UboPacking.WriteVec4(data,MoonDirectionOffset,light.Moon.X,light.Moon.Y,light.Moon.Z,0);
        UboPacking.WriteVec4(data,SolarOffset,light.Solar.X,light.Solar.Y,light.Solar.Z,0);
        UboPacking.WriteVec4(data,LunarOffset,light.Lunar.X,light.Lunar.Y,light.Lunar.Z,0);
        UboPacking.WriteVec4(data,EnvironmentOffset,light.Environment.X,light.Environment.Y,light.Environment.Z,0);
        UboPacking.WriteVec4(data,EncodingCapOffset,target.Cap.X,target.Cap.Y,target.Cap.Z,0);
        UboPacking.WriteVec4(data,EncodingScaleOffset,batch.Fields.Encoding.Scale,target.Scale,0,0);SetBytes(data);
    }
    #endregion
}
