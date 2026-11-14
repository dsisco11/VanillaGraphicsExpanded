using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the complete std140 layout for cloud volume copy parameters.</summary>
internal sealed class CloudVolumeCopyParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 48;
    internal const int GridOffset = 0;
    internal const int ScaleOffset = 16;
    internal const int CapOffset = 32;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages this complete layout from one coherent set of semantic inputs.</summary>
    internal void Capture(CloudVolumeDomain domain, CloudVolumeEncoding source, CloudVolumeEncoding target, bool repair = false)
    {
         Span<byte> data = stackalloc byte[BlockSize]; data.Clear();
        UboPacking.WriteIVec4(data, GridOffset, domain.Size, CloudVolumeDomain.Height, domain.Size, repair ? 1 : 0);
        UboPacking.WriteVec4(data, ScaleOffset, source.Scale, target.Scale, 0, 0);
        UboPacking.WriteVec4(data, CapOffset, target.Cap.X, target.Cap.Y, target.Cap.Z, 0); SetBytes(data);
    }
    #endregion
}
