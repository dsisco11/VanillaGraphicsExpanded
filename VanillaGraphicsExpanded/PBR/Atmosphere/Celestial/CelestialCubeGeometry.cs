using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Shares OpenGL face directions and exact texel solid angles across generation and integration.</summary>
internal static class CelestialCubeGeometry
{
    #region Public API
    /// <summary>Maps face-local coordinates in [-1,1] to the common right-handed celestial frame.</summary>
    internal static Vector3 Direction(int face, float u, float v) => Vector3.Normalize(face switch
    {
        0 => new(1, -v, -u), 1 => new(-1, -v, u), 2 => new(u, 1, v),
        3 => new(u, -1, -v), 4 => new(u, -v, 1), 5 => new(-u, -v, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(face))
    });

    /// <summary>Integrates the cube projection Jacobian over one pixel, including non-power-of-two faces.</summary>
    internal static float SolidAngle(int x, int y, int size)
    {
        double u0 = 2.0 * x / size - 1, v0 = 2.0 * y / size - 1;
        double u1 = 2.0 * (x + 1) / size - 1, v1 = 2.0 * (y + 1) / size - 1;
        return (float)(Area(u1, v1) - Area(u0, v1) - Area(u1, v0) + Area(u0, v0));
    }
    #endregion

    #region Private
    /// <summary>Evaluates the analytic corner primitive of cube solid angle.</summary>
    private static double Area(double u, double v) => Math.Atan2(u * v, Math.Sqrt(1 + u * u + v * v));
    #endregion
}
