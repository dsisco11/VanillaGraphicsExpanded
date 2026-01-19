using System;

using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal static class LumOnWorldProbeTraceDirections
{
    // Keep in sync with LUMON_OCTAHEDRAL_SIZE in lumon_octahedral.glsl.
    public const int OctahedralSize = 8;

    private static readonly Vec3f[] Directions = BuildDirections();

    public static ReadOnlySpan<Vec3f> GetDirections() => Directions;

    private static Vec3f[] BuildDirections()
    {
        int n = OctahedralSize;
        var dirs = new Vec3f[n * n];

        int i = 0;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                // Texel-center UV in [0,1].
                float u = (x + 0.5f) / n;
                float v = (y + 0.5f) / n;

                dirs[i++] = OctahedralUVToDirection(u, v);
            }
        }

        return dirs;
    }

    private static Vec3f OctahedralUVToDirection(float u, float v)
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
        if (len < 1e-6f) return new Vec3f(0, 1, 0);

        return new Vec3f(x / len, y / len, z / len);
    }
}
