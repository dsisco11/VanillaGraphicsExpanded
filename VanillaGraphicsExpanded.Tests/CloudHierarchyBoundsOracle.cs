using System.Numerics;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Enumerates native descendants directly, independently of hierarchy layout and reduction helpers.</summary>
internal static class CloudHierarchyBoundsOracle
{
    #region Public API
    /// <summary>Computes exact summary extrema for a native-cell rectangle, treating outside cells as empty.</summary>
    internal static (float Lower, float Upper) Bounds(Vector4[] columns, int width, int x, int z, int span)
    {
        float lower = float.PositiveInfinity, upper = float.NegativeInfinity;
        // No pyramid or production interval helper participates in this reference calculation.
        for (int row = Math.Max(0, z); row < Math.Min(width, z + span); row++)
        for (int col = Math.Max(0, x); col < Math.Min(width, x + span); col++)
        {
            Vector4 value = columns[row * width + col];
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)
                || !float.IsFinite(value.W) || value.X <= 0 || !(value.W > value.Z)) continue;
            lower = Math.Min(lower, value.Z * 50f);
            upper = Math.Max(upper, value.W * 50f);
        }
        return (lower, upper);
    }
    #endregion
}
