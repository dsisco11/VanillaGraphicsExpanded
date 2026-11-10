using System;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Projections;

/// <summary>Double-precision projection primitives with explicit representable-address checks.</summary>
internal static class ShadowProjectionMath
{
    #region Public API
    /// <summary>Rejects nonfinite vectors at the world/projection boundary.</summary>
    internal static bool Finite(Vector3d v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
    /// <summary>Computes the right-handed perpendicular used by the light basis.</summary>
    internal static Vector3d Cross(Vector3d a, Vector3d b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    /// <summary>Normalizes a finite nonzero direction without accepting an undefined light basis.</summary>
    internal static Vector3d Unit(Vector3d v)
    {
        double length = Math.Sqrt(Vector3d.Dot(v, v));
        if (!Finite(v) || !double.IsFinite(length) || length < 1e-12) throw new ArgumentOutOfRangeException(nameof(v));
        return v / length;
    }
    /// <summary>Enumerates eight corners; extrema of an affine projection occur at these corners.</summary>
    internal static Vector3d Corner(ShadowBounds b, int i) => new((i & 1) == 0 ? b.MinX : b.MaxX,
        (i & 2) == 0 ? b.MinY : b.MaxY, (i & 4) == 0 ? b.MinZ : b.MaxZ);
    /// <summary>Uses mathematical floor for negative virtual coordinates and rejects overflow.</summary>
    internal static int Page(double texel, int size)
    {
        double page = Math.Floor(texel / size);
        if (!double.IsFinite(page) || page < int.MinValue || page > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(texel));
        return (int)page;
    }
    #endregion
}
