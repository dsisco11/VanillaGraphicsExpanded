using System;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Projections;

/// <summary>A bounded rectangle in virtual texels, independent of physical tile placement.</summary>
internal readonly record struct ShadowRasterRegion(long X, long Y, int Width, int Height)
{
    #region Public API
    /// <summary>Rejects empty or overflowing regions before projection and shader address conversion.</summary>
    internal void Validate()
    {
        if (Width <= 0 || Height <= 0 || X > long.MaxValue - Width || Y > long.MaxValue - Height)
            throw new ArgumentOutOfRangeException(nameof(Width), "Virtual raster region is empty or exceeds host addressing.");
    }

    /// <summary>Requires the complete candidate rectangle to fit, without clipping or truncating it.</summary>
    internal bool Contains(ShadowRasterRegion other) => Width > 0 && Height > 0 &&
        other.Width > 0 && other.Height > 0 && X <= long.MaxValue - Width && Y <= long.MaxValue - Height &&
        other.X <= long.MaxValue - other.Width && other.Y <= long.MaxValue - other.Height &&
        other.X >= X && other.Y >= Y && other.X + other.Width <= X + Width &&
        other.Y + other.Height <= Y + Height;
    #endregion
}
