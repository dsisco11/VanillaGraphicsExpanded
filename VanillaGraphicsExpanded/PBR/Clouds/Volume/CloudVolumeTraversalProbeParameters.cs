using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the complete std140 layout for cloud volume traversal probe parameters.</summary>
internal sealed class CloudVolumeTraversalProbeParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 128;
    internal const int GridOffset = 0;
    internal const int SlotsOffset = 16;
    internal const int OriginOffset = 32;
    internal const int CellSizeOffset = 48;
    internal const int BoundsMinOffset = 64;
    internal const int BoundsMaxOffset = 80;
    internal const int RayOriginOffset = 96;
    internal const int RayDirectionOffset = 112;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages this complete layout from one coherent set of semantic inputs.</summary>
    internal void Capture(CloudVolumeDomain d,Vector3 origin,Vector3 direction,float length,int limit=0)
    {
        Span<byte> data=stackalloc byte[BlockSize];data.Clear();
        UboPacking.WriteIVec4(data,GridOffset,d.Size,CloudVolumeDomain.Height,d.Size,limit);
        UboPacking.WriteIVec4(data,SlotsOffset,d.SlotX,d.SlotZ,d.BlocksX,d.BlocksY);
        UboPacking.WriteVec4(data,OriginOffset,(float)(d.FirstX*CloudVolumeDomain.HorizontalCell-d.AnchorX),(float)(d.Bottom-d.VerticalCell),
            (float)(d.FirstZ*CloudVolumeDomain.HorizontalCell-d.AnchorZ),0);
        UboPacking.WriteVec4(data,CellSizeOffset,(float)CloudVolumeDomain.HorizontalCell,d.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,0);
        UboPacking.WriteVec4(data,BoundsMinOffset,0,(float)d.Bottom,0,0);
        UboPacking.WriteVec4(data,BoundsMaxOffset,50*d.Width,(float)(d.Bottom+d.Band),50*d.Width,0);
        UboPacking.WriteVec4(data,RayOriginOffset,origin.X,origin.Y,origin.Z,length);
        UboPacking.WriteVec4(data,RayDirectionOffset,direction.X,direction.Y,direction.Z,0);SetBytes(data);
    }
    #endregion
}
