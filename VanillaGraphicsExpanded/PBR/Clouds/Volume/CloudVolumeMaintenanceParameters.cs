using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the complete std140 layout for cloud volume maintenance parameters.</summary>
internal sealed class CloudVolumeMaintenanceParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 80;
    internal const int GridOffset = 0;
    internal const int SlotsOffset = 16;
    internal const int RetainedOriginOffset = 32;
    internal const int RangeOffset = 48;
    internal const int FallbackOffset = 64;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages this complete layout from one coherent set of semantic inputs.</summary>
    internal void Capture(CloudVolumeDomain target, CloudVolumeDomain? source, Vector3 fallback, int start, int count)
    {
         Span<byte> data = stackalloc byte[BlockSize]; data.Clear();
        UboPacking.WriteIVec4(data, GridOffset, target.Size, CloudVolumeDomain.Height, target.Size, source is null ? 0 : 1);
        UboPacking.WriteIVec4(data, SlotsOffset, target.SlotX, 0, target.SlotZ, 0);
        UboPacking.WriteIVec4(data, RetainedOriginOffset, source is null ? 0 : checked((int)(target.FirstX-source.FirstX)), 0,
            source is null ? 0 : checked((int)(target.FirstZ-source.FirstZ)), 0);
        UboPacking.WriteIVec4(data, RangeOffset, start, count, target.BlocksX, target.BlocksY);
        UboPacking.WriteVec4(data, FallbackOffset, fallback.X, fallback.Y, fallback.Z, 0); SetBytes(data);
    }
    #endregion
}
