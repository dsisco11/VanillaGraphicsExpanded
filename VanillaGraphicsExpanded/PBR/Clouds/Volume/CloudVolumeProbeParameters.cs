using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns the complete std140 layout for cloud volume probe parameters.</summary>
internal sealed class CloudVolumeProbeParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 16;
    internal const int QueryOffset = 0;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages this complete layout from one coherent set of semantic inputs.</summary>
    internal void Capture(CloudVolumeViewFields fields, Vector3 native)
    {
         var uv = fields.Domain.TexturePosition(native); Span<byte> data = stackalloc byte[BlockSize];
        UboPacking.WriteVec4(data, QueryOffset, uv.X, uv.Y, uv.Z, fields.Encoding.Scale); SetBytes(data);
    }
    #endregion
}
