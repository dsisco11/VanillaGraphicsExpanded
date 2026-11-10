using System;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Projections;

/// <summary>Builds homogeneous page projections for already-deformed positions relative to one frozen render origin.</summary>
internal static class ShadowRasterProjection
{
    #region Public API
    /// <summary>Uses the same absolute light basis, page address and conventional depth as directional sampling.</summary>
    internal static float[] Directional(DirectionalShadowProjection projection, ShadowPageKey page, Vector3d origin)
    {
        if (page.Source != projection.Source || page.Projection != projection.Generation)
            throw new ArgumentException("Page and directional projection disagree.", nameof(page));
        double width = projection.TexelWidth(page.FaceOrLevel) * projection.PageSize;
        var x = projection.Right * (2 / width);
        var y = projection.Up * (2 / width);
        var z = projection.Forward * (2 / (projection.Far - projection.Near));
        return Matrix(x, y, z, default,
            Vector3d.Dot(origin, x) - (2.0 * page.X + 1),
            Vector3d.Dot(origin, y) - (2.0 * page.Y + 1),
            Vector3d.Dot(origin, z) - 2 * projection.Near / (projection.Far - projection.Near) - 1, 1);
    }

    /// <summary>Retains homogeneous W for near/source crossings instead of dividing positions before clipping.</summary>
    internal static float[] Point(PointShadowProjection projection, ShadowPageKey page, Vector3d origin)
    {
        if (page.Source != projection.Source || page.Projection != projection.Generation
            || page.X < 0 || page.Y < 0 || page.X >= projection.FaceResolution / projection.PageSize
            || page.Y >= projection.FaceResolution / projection.PageSize)
            throw new ArgumentException("Page and point projection disagree.", nameof(page));
        var forward = PointShadowProjection.Direction(page.FaceOrLevel, 0, 0);
        var right = PointShadowProjection.Direction(page.FaceOrLevel, 1, 0) - forward;
        var up = PointShadowProjection.Direction(page.FaceOrLevel, 0, 1) - forward;
        double scale = (double)projection.FaceResolution / projection.PageSize;
        var x = right * scale + forward * (scale - 2.0 * page.X - 1);
        var y = up * scale + forward * (scale - 2.0 * page.Y - 1);
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
