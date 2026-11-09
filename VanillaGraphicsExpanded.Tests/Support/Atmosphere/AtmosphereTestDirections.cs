using System.Numerics;

namespace VanillaGraphicsExpanded.Tests.Support.Atmosphere;

/// <summary>Creates deterministic ray samples shared by atmosphere test boundaries.</summary>
internal static class AtmosphereTestDirections
{
    #region Internal API
    /// <summary>Returns exact and near-horizon directions with varied non-unit lengths.</summary>
    internal static Vector3[] Create(int count)
    {
        var result = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float y = (i % 8) switch { 0 => 0, 1 => .0001f, 2 => -.0001f, _ => 2f * (i + .5f) / count - 1f };
            float azimuth = i * 2.39996323f;
            float horizontal = MathF.Sqrt(MathF.Max(0, 1f - y * y));
            result[i] = new Vector3(MathF.Cos(azimuth) * horizontal, y, MathF.Sin(azimuth) * horizontal) * (1f + i % 5);
        }
        return result;
    }
    #endregion
}
