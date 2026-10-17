using System.Numerics;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Enumerates every native column box in double precision without DDA, hierarchy or production helpers.</summary>
internal static class CloudTraversalOracle
{
    #region Public API
    /// <summary>Returns ordered positive-length occupied intersections under the native start ownership and parallel threshold.</summary>
    internal static List<(double Near, double Far, int X, int Z)> Intersections(Vector4[] columns,
        int width, Vector3 origin, Vector3 direction, float limit)
    {
        var result = new List<(double Near, double Far, int X, int Z)>();
        double start = 0, end = limit;
        if (!Clip(origin.X, direction.X, 0, width * 50, ref start, ref end)
            || !Clip(origin.Z, direction.Z, 0, width * 50, ref start, ref end)) return result;
        // CloudStart assigns its first XZ cell just inside entry. Preserve that ownership policy,
        // but compute each descendant's independent geometric slab intersection rather than walking clocks.
        Vector3 entry = origin + direction * ((float)start + 1e-4f);
        int firstX = Math.Clamp((int)MathF.Floor(entry.X / 50), 0, width - 1);
        int firstZ = Math.Clamp((int)MathF.Floor(entry.Z / 50), 0, width - 1);
        for (int z = 0; z < width; z++)
        for (int x = 0; x < width; x++)
        {
            if (!ForwardCell(x, firstX, direction.X) || !ForwardCell(z, firstZ, direction.Z)) continue;
            Vector4 column = columns[z * width + x];
            if (!float.IsFinite(column.X) || !float.IsFinite(column.Y) || !float.IsFinite(column.Z)
                || !float.IsFinite(column.W) || column.X <= 0 || column.W <= column.Z) continue;
            double near = start, far = end;
            if (Clip(origin.X, direction.X, x * 50, (x + 1) * 50, ref near, ref far)
                && Clip(origin.Z, direction.Z, z * 50, (z + 1) * 50, ref near, ref far)
                && Clip(origin.Y, direction.Y, column.Z * 50f, column.W * 50f, ref near, ref far) && far > near)
                result.Add((near, far, x, z));
        }
        return result.OrderBy(value => value.Near).ThenBy(value => value.Far).ToList();
    }

    /// <summary>Returns a direct cell-box projection, including tangencies or a negative overlap for near misses.</summary>
    internal static (double Near, double Far) CellBounds(Vector4 column, int x, int z,
        Vector3 origin, Vector3 direction, float limit)
    {
        double near = 0, far = limit;
        // Apply all constraints even if one creates a negative overlap: its gap independently
        // bounds any float-recurrence sliver that the native DDA reports near a geometric corner.
        Clip(origin.X, direction.X, x * 50, (x + 1) * 50, ref near, ref far);
        Clip(origin.Z, direction.Z, z * 50, (z + 1) * 50, ref near, ref far);
        Clip(origin.Y, direction.Y, column.Z * 50f, column.W * 50f, ref near, ref far);
        return (near, far);
    }

    /// <summary>Bounds native clock rounding from slab subtraction/division and at most width positive repeated additions.</summary>
    internal static double ClockError(int width, double time)
    {
        const double unitRoundoff = 1.0 / 16777216;
        double operations = width + 4;
        double gamma = operations * unitRoundoff / (1 - operations * unitRoundoff);
        return gamma * Math.Max(1, Math.Abs(time));
    }
    #endregion

    #region Private
    /// <summary>Respects initial half-open cell ownership, including axes treated as exactly parallel by the shader.</summary>
    private static bool ForwardCell(int cell, int first, float direction) => Math.Abs(direction) < 1e-8f
        ? cell == first : direction > 0 ? cell >= first : cell <= first;

    /// <summary>Clips a direct geometric segment against one slab, independently of native recurrence clocks.</summary>
    private static bool Clip(double origin, float direction, double lower, double upper, ref double near, ref double far)
    {
        if (Math.Abs(direction) < 1e-8f)
        {
            if (origin >= lower && origin <= upper) return true;
            near = double.PositiveInfinity; far = double.NegativeInfinity;
            return false;
        }
        double first = (lower - origin) / direction, second = (upper - origin) / direction;
        near = Math.Max(near, Math.Min(first, second));
        far = Math.Min(far, Math.Max(first, second));
        return far >= near;
    }
    #endregion
}
