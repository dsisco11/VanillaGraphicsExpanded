using System;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Projections;

/// <summary>Builds homogeneous virtual-region projections for deformed positions relative to a frozen world origin.</summary>
internal static class ShadowRasterProjection
{
    #region Public API
    /// <summary>Projects a whole virtual region with world-consistent texel density and conventional depth.</summary>
    internal static float[] Directional(DirectionalShadowProjection projection, int level, ShadowRasterRegion region, Vector3d origin)
    {
        region.Validate();
        double texel = projection.TexelWidth(level);
        var x = projection.Right * (2 / (texel * region.Width));
        var y = projection.Up * (2 / (texel * region.Height));
        var z = projection.Forward * (2 / (projection.Far - projection.Near));
        return Matrix(x, y, z, default,
            Vector3d.Dot(origin, x) - (2.0 * region.X / region.Width + 1),
            Vector3d.Dot(origin, y) - (2.0 * region.Y / region.Height + 1),
            Vector3d.Dot(origin, z) - 2 * projection.Near / (projection.Far - projection.Near) - 1, 1);
    }

    /// <summary>Retains homogeneous W for near/source crossings instead of dividing positions before clipping.</summary>
    internal static float[] Point(PointShadowProjection projection, int face, ShadowRasterRegion region, Vector3d origin)
    {
        region.Validate();
        if (!new ShadowRasterRegion(0, 0, projection.FaceResolution, projection.FaceResolution).Contains(region))
            throw new ArgumentException("Virtual raster region exceeds its point face.", nameof(region));
        var forward = PointShadowProjection.Direction(face, 0, 0);
        var right = PointShadowProjection.Direction(face, 1, 0) - forward;
        var up = PointShadowProjection.Direction(face, 0, 1) - forward;
        double sx = (double)projection.FaceResolution / region.Width, sy = (double)projection.FaceResolution / region.Height;
        var x = right * sx + forward * (sx - 2.0 * region.X / region.Width - 1);
        var y = up * sy + forward * (sy - 2.0 * region.Y / region.Height - 1);
        var z = forward * ((projection.Far + projection.Near) / (projection.Far - projection.Near));
        var relative = origin - projection.Position;
        return Matrix(x, y, z, forward, Vector3d.Dot(relative, x), Vector3d.Dot(relative, y),
            Vector3d.Dot(relative, z) - 2 * projection.Far * projection.Near / (projection.Far - projection.Near),
            Vector3d.Dot(relative, forward));
    }
    #endregion

    #region Private
    /// <summary>Packs column-major coefficients and rejects conversions which cannot represent a finite projection.</summary>
    private static float[] Matrix(Vector3d x, Vector3d y, Vector3d z, Vector3d w,
        double tx, double ty, double tz, double tw)
    {
        double[] values = [x.X, y.X, z.X, w.X, x.Y, y.Y, z.Y, w.Y, x.Z, y.Z, z.Z, w.Z, tx, ty, tz, tw];
        var result = new float[16];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = (float)values[i];
            if (!float.IsFinite(result[i])) throw new ArgumentOutOfRangeException(nameof(tx), "Raster projection exceeds finite float range.");
        }
        return result;
    }
    #endregion
}
