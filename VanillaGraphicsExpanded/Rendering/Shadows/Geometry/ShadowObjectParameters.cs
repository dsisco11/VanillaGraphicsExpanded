using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns the complete std140 layout for shadow object parameters.</summary>
internal sealed class ShadowObjectParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 16;
    internal const int FlagsOffset = 0;
    internal const int WarpSuppressionOffset = 4;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages object flags and deformation suppression with deterministic unused lanes.</summary>
    internal void SetFlags(int flags, int warpSuppression = 0)
    {
        // Reject unsupported native deformation modes before changing retained input bytes.
        if (warpSuppression is < 0 or > 2)
            throw new System.ArgumentOutOfRangeException(nameof(warpSuppression));
        WriteIntVector4(FlagsOffset, flags, warpSuppression, 0, 0);
    }
    #endregion
}
