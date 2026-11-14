using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the complete std140 layout for cloud volume work parameters.</summary>
internal sealed class CloudVolumeWorkParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 208;
    internal const int GridOffset = 0;
    internal const int IdentityOffset = 16;
    internal const int CompletionOffset = 32;
    internal const int OriginOffset = 48;
    internal const int CellSizeOffset = 64;
    internal const int LightingOffset = 80;
    internal const int DispatchOffset = 96;
    internal const int PlanesOffset = 112;
    internal const int PlaneCount = 6;
    internal const int PlaneStride = 16;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages this complete layout from one coherent set of semantic inputs.</summary>
    internal void Capture(CloudVolumeDomain domain,int diffuseSlices,uint frame,uint revision,int active,int dirty,int newlyActive,
        bool diffuse,int completed,ReadOnlySpan<Vector4> planes,int source=0,int destination=0,int directGroups=0,int diffuseGroups=0,
        bool densityPending=false,int lightingRays=CloudVolumeLightingBudget.DefaultRays,int selectionLevel=0)
    {
        Span<byte> data=stackalloc byte[BlockSize];data.Clear();
        UboPacking.WriteIVec4(data,GridOffset,domain.BlocksX,domain.BlocksY,domain.BlocksX,domain.Blocks);
        UboPacking.WriteIVec4(data,IdentityOffset,unchecked((int)frame),unchecked((int)revision),active,dirty);
        UboPacking.WriteIVec4(data,CompletionOffset,domain.SlotX,domain.SlotZ,domain.Size,completed);
        UboPacking.WriteVec4(data,OriginOffset,(float)(domain.FirstX*CloudVolumeDomain.HorizontalCell-domain.AnchorX),
            (float)(domain.Bottom-domain.VerticalCell),(float)(domain.FirstZ*CloudVolumeDomain.HorizontalCell-domain.AnchorZ),lightingRays/CloudVolumeLightingBudget.RaysPerUnit);
        UboPacking.WriteVec4(data,CellSizeOffset,(float)CloudVolumeDomain.HorizontalCell,domain.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,densityPending?1:0);
        UboPacking.WriteIVec4(data,LightingOffset,newlyActive,diffuse?1:0,selectionLevel,diffuseSlices);
        UboPacking.WriteIVec4(data,DispatchOffset,source,destination,directGroups,diffuseGroups);
        for(int i=0;i<PlaneCount;i++)UboPacking.WriteVec4(data,PlanesOffset+PlaneStride*i,planes[i].X,planes[i].Y,planes[i].Z,planes[i].W);
        SetBytes(data);
    }
    #endregion
}
