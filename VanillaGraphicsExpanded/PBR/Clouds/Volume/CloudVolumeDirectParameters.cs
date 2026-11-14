using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the complete std140 layout for cloud volume direct parameters.</summary>
internal sealed class CloudVolumeDirectParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 128;
    internal const int GridOffset = 0;
    internal const int SlotsOffset = 16;
    internal const int OriginOffset = 32;
    internal const int CellSizeOffset = 48;
    internal const int BoundsMinOffset = 64;
    internal const int BoundsMaxOffset = 80;
    internal const int DirectionOffset = 96;
    internal const int ActivationOffset = 112;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages this complete layout from one coherent set of semantic inputs.</summary>
    internal void Capture(CloudVolumeDomain d,Vector3 direction,int newlyActive)
    {
        Span<byte> data=stackalloc byte[BlockSize];data.Clear();
        UboPacking.WriteIVec4(data,GridOffset,d.Size,CloudVolumeDomain.Height,d.Size,0);
        UboPacking.WriteIVec4(data,SlotsOffset,d.SlotX,d.SlotZ,d.BlocksX,d.BlocksY);
        UboPacking.WriteVec4(data,OriginOffset,(float)(d.FirstX*CloudVolumeDomain.HorizontalCell-d.AnchorX),(float)(d.Bottom-d.VerticalCell),
            (float)(d.FirstZ*CloudVolumeDomain.HorizontalCell-d.AnchorZ),0);
        UboPacking.WriteVec4(data,CellSizeOffset,(float)CloudVolumeDomain.HorizontalCell,d.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,0);
        UboPacking.WriteVec4(data,BoundsMinOffset,0,(float)d.Bottom,0,0);
        UboPacking.WriteVec4(data,BoundsMaxOffset,50*d.Width,(float)(d.Bottom+d.Band),50*d.Width,0);
        UboPacking.WriteVec4(data,DirectionOffset,direction.X,direction.Y,direction.Z,0);
        UboPacking.WriteIVec4(data,ActivationOffset,newlyActive,0,0,0);SetBytes(data);
    }
    #endregion
}
