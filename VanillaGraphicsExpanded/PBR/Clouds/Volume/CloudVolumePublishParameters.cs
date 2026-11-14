using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the complete std140 layout for cloud volume publish parameters.</summary>
internal sealed class CloudVolumePublishParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 16;
    internal const int GridOffset = 0;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages this complete layout from one coherent set of semantic inputs.</summary>
    internal void Capture(CloudVolumeDomain domain,int full)
    {
        Span<byte> data=stackalloc byte[BlockSize];
        UboPacking.WriteIVec4(data,GridOffset,domain.Size,domain.BlocksX,domain.BlocksY,full);SetBytes(data);
    }
    #endregion
}
