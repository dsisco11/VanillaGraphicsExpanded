using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Packs bounded native-local coordinates and a coherent field's physical decode information.</summary>
internal sealed class LightingVolumeParameters : CpuUniformBuffer
{
    #region Public API
    /// <summary>Allocates only the four candidate-owned parameter vectors.</summary>
    internal LightingVolumeParameters() : base(64) { }
    /// <summary>Converts large material coordinates on the CPU and publishes bounded GPU-relative offsets.</summary>
    internal void Capture(LightingVolumeDomain domain, CloudPlacement placement, LightingVolumeEncoding encoding)
    {
        Span<byte> data = stackalloc byte[64];
        Vector3 first = domain.FirstNode;
        UboPacking.WriteVec4(data, 0, placement.Offset.X, placement.Offset.Y, placement.Offset.Z, domain.Width);
        UboPacking.WriteVec4(data, 16, (float)(domain.AnchorX % 12800), (float)(domain.AnchorZ % 12800), 1, 0);
        UboPacking.WriteVec4(data, 32, first.X, first.Y, first.Z, encoding.Scale);
        UboPacking.WriteVec4(data, 48, domain.Size, LightingVolumeDomain.Height, domain.Size, encoding.Cap);
        data.CopyTo(DataWritable); MarkDirty();
    }
    #endregion
}
