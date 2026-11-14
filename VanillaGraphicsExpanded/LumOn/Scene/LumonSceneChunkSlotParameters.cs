using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Owns the std140 terrain chunk-slot mapping supplied to native terrain programs.</summary>
internal sealed class LumonSceneChunkSlotParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 48;
    internal const int OriginOffset = 0;
    internal const int DimensionsOffset = 16;
    internal const int RingOffset = 32;

    /// <summary>Declares the three complete std140 integer-vector lanes.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages one coherent mapping without changing its native GPU publication lifetime.</summary>
    internal void Set(VectorInt3 origin, VectorInt3 dimensions, VectorInt3 ring)
    {
        WriteIntVector4(OriginOffset, origin.X, origin.Y, origin.Z, 0);
        WriteIntVector4(DimensionsOffset, dimensions.X, dimensions.Y, dimensions.Z, 0);
        WriteIntVector4(RingOffset, ring.X, ring.Y, ring.Z, 0);
    }
    #endregion
}
