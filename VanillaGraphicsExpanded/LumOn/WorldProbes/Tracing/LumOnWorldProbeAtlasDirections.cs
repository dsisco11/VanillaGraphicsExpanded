using System;
using System.Numerics;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>
/// Direction table for the future world-probe octahedral atlas payload (SH replacement).
/// This is intentionally separate from <see cref="LumOnWorldProbeTraceDirections"/>, which currently
/// drives SH-based integration for the existing world-probe pipeline.
/// </summary>
internal static class LumOnWorldProbeAtlasDirections
{
    private static readonly object CacheLock = new();
    private static readonly System.Collections.Generic.Dictionary<int, Vector3[]> DirectionsBySize = new();

    public static ReadOnlySpan<Vector3> GetDirections(int octahedralSize)
    {
        if (octahedralSize <= 0) throw new ArgumentOutOfRangeException(nameof(octahedralSize));

        lock (CacheLock)
        {
            if (!DirectionsBySize.TryGetValue(octahedralSize, out var dirs))
            {
                dirs = BuildDirections(octahedralSize);
                DirectionsBySize[octahedralSize] = dirs;
            }

            return dirs;
        }
    }

    public static Vector3 GetDirectionForTexelCenter(int octahedralSize, int octX, int octY)
    {
        if (octahedralSize <= 0) throw new ArgumentOutOfRangeException(nameof(octahedralSize));

        if ((uint)octX >= (uint)octahedralSize || (uint)octY >= (uint)octahedralSize)
        {
            return Vector3.UnitY;
        }

        var dirs = GetDirections(octahedralSize);
        int idx = octY * octahedralSize + octX;
        return dirs[idx];
    }

    private static Vector3[] BuildDirections(int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));

        var dirs = new Vector3[size * size];

        int i = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Texel-center UV in [0,1].
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;

                dirs[i++] = OctahedralUVToDirection(u, v);
            }
        }

        return dirs;
    }

    private static Vector3 OctahedralUVToDirection(float u, float v)
    {
        // Matches lumonOctahedralUVToDirection() in lumon_octahedral.glsl.
        float ox = u * 2f - 1f;
        float oy = v * 2f - 1f;

        float x = ox;
        float y = oy;
        float z = 1f - Math.Abs(x) - Math.Abs(y);

        if (z < 0f)
        {
            float nx = (1f - Math.Abs(y)) * (x >= 0f ? 1f : -1f);
            float ny = (1f - Math.Abs(x)) * (y >= 0f ? 1f : -1f);
            x = nx;
            y = ny;
        }

        float len = MathF.Sqrt(x * x + y * y + z * z);
        if (len < 1e-6f) return Vector3.UnitY;

        return new Vector3(x / len, y / len, z / len);
    }
}

