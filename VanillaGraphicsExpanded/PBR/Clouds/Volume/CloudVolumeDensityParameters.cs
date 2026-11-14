using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the complete std140 layout for cloud volume density parameters.</summary>
internal sealed class CloudVolumeDensityParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 128;
    internal const int GridOffset = 0;
    internal const int SlotsOffset = 16;
    internal const int HistoryOffset = 32;
    internal const int OriginOffset = 48;
    internal const int CellSizeOffset = 64;
    internal const int IdentityOffset = 80;
    internal const int BudgetOffset = 96;
    internal const int NoiseOriginOffset = 112;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages this complete layout from one coherent set of semantic inputs.</summary>
    internal void Capture(CloudVolumeDomain domain,long x,long z,CloudVolumeDensityState state,uint frame,bool initial,int radixShift=0)
    {
        Span<byte> data=stackalloc byte[BlockSize];data.Clear();
        bool history=state.SnapshotValid && Math.Abs(x-state.WindowX)<domain.Width && Math.Abs(z-state.WindowZ)<domain.Width;
        UboPacking.WriteIVec4(data,GridOffset,domain.Size,CloudVolumeDomain.Height,domain.Width,domain.BlocksX);
        UboPacking.WriteIVec4(data,SlotsOffset,domain.SlotX,domain.SlotZ,CloudVolumeDomain.Mod(x,domain.Width),CloudVolumeDomain.Mod(z,domain.Width));
        UboPacking.WriteIVec4(data,HistoryOffset,history?checked((int)(state.WindowX-x)):0,history?checked((int)(state.WindowZ-z)):0,history?1:0,state.LayoutChanged?1:0);
        UboPacking.WriteVec4(data,OriginOffset,(float)(domain.FirstX*CloudVolumeDomain.HorizontalCell-domain.AnchorX),
            (float)(domain.Bottom-domain.VerticalCell),(float)(domain.FirstZ*CloudVolumeDomain.HorizontalCell-domain.AnchorZ),0);
        UboPacking.WriteVec4(data,CellSizeOffset,(float)CloudVolumeDomain.HorizontalCell,domain.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,0);
        UboPacking.WriteIVec4(data,IdentityOffset,unchecked((int)frame),unchecked((int)state.Revision),domain.Blocks,initial?1:0);
        UboPacking.WriteIVec4(data,BudgetOffset,4096,domain.BlocksY,unchecked((int)state.SnapshotFrame),radixShift);
        UboPacking.WriteVec4(data,NoiseOriginOffset,(float)((domain.AnchorX%12800+12800)%12800),0,
            (float)((domain.AnchorZ%12800+12800)%12800),0);SetBytes(data);
    }
    #endregion
}
