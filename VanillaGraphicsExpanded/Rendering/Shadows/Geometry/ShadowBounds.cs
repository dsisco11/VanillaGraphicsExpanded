using System;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Conservative world-space bounds in double precision, including deformation where applicable.</summary>
internal readonly record struct ShadowBounds(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
    /// <summary>Rejects inverted, infinite and unknown extents instead of treating them as empty.</summary>
    internal bool IsValid => double.IsFinite(MinX) && double.IsFinite(MinY) && double.IsFinite(MinZ)
        && double.IsFinite(MaxX) && double.IsFinite(MaxY) && double.IsFinite(MaxZ)
        && MinX <= MaxX && MinY <= MaxY && MinZ <= MaxZ;

    #region Public API
    /// <summary>Includes boundary contact when checking a previously certified receiving region.</summary>
    internal bool Contains(ShadowBounds other) => IsValid && other.IsValid && MinX <= other.MinX
        && MinY <= other.MinY && MinZ <= other.MinZ && MaxX >= other.MaxX && MaxY >= other.MaxY && MaxZ >= other.MaxZ;
    #endregion
}
