using System;
using System.Collections.Generic;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Bins omitted stars into bounded tiles and normalizes each spherical cap over all six faces.</summary>
internal sealed class CelestialResidual
{
    internal const int TileSize = 128;
    internal static readonly float Height = 1 - MathF.Cos(MathF.PI / 90);
    internal static readonly float KernelMaximum = 1 / (MathF.PI * Height);
    internal float[] Catalog { get; } = new float[CelestialSource.StarCapacity * 8];
    internal List<int>[] Tiles { get; }

    #region Public API
    /// <summary>Prepares exact discrete kernel normalization and tile indices without scanning the full map per star.</summary>
    internal CelestialResidual(CelestialSource source)
    {
        int size = source.Quality.FaceSize, count = (size + TileSize - 1) / TileSize;
        Tiles = new List<int>[6 * count * count];
        for (int i = 0; i < Tiles.Length; i++) Tiles[i] = new();
        for (int index = 0; index < source.Stars.Length; index++)
        {
            var star = source.Stars[index];
            Catalog[index * 8] = star.Direction.X; Catalog[index * 8 + 1] = star.Direction.Y; Catalog[index * 8 + 2] = star.Direction.Z;
            if (index < source.Quality.StarCount) continue;
            double integral = 0;
            for (int face = 0; face < 6; face++)
            {
                var bounds = Bounds(star.Direction, face, size);
                if (bounds.X1 < bounds.X0 || bounds.Y1 < bounds.Y0) continue;
                for (int ty = bounds.Y0 / TileSize; ty <= bounds.Y1 / TileSize; ty++)
                for (int tx = bounds.X0 / TileSize; tx <= bounds.X1 / TileSize; tx++) Tiles[face * count * count + ty * count + tx].Add(index);
                for (int y = bounds.Y0; y <= bounds.Y1; y++) for (int x = bounds.X0; x <= bounds.X1; x++)
                {
                    Vector3 d = CelestialCubeGeometry.Direction(face, 2f * (x + .5f) / size - 1, 2f * (y + .5f) / size - 1);
                    integral += Kernel(d, star.Direction) * CelestialCubeGeometry.SolidAngle(x, y, size);
                }
            }
            if (!double.IsFinite(integral) || integral < .95 || integral > 1.05) throw new ArgumentException("Residual cap normalization is outside its admitted bound.");
            Vector3 amplitude = star.Color * (float)(star.FluxLux / integral);
            Catalog[index * 8 + 4] = amplitude.X; Catalog[index * 8 + 5] = amplitude.Y; Catalog[index * 8 + 6] = amplitude.Z;
        }
    }

    /// <summary>Evaluates a compact nonnegative two-degree kernel normalized over solid angle.</summary>
    internal static float Kernel(Vector3 sample, Vector3 star) => MathF.Max(0, 1 - (1 - Vector3.Dot(sample, star)) / Height) * KernelMaximum;

    /// <summary>Conservatively projects a spherical cap onto a selected face with explicit cross-face support.</summary>
    internal static (int X0, int Y0, int X1, int Y1) Bounds(Vector3 star, int face, int size)
    {
        Vector3 normal = CelestialCubeGeometry.Direction(face, 0, 0);
        Vector3 right = Vector3.Normalize(CelestialCubeGeometry.Direction(face, 1, 0) * MathF.Sqrt(2) - normal);
        Vector3 up = Vector3.Normalize(CelestialCubeGeometry.Direction(face, 0, 1) * MathF.Sqrt(2) - normal);
        float n = Vector3.Dot(star, normal), a = Vector3.Dot(star, right), b = Vector3.Dot(star, up), s = MathF.Sin(MathF.PI / 90);
        if (n <= s) return (1, 1, 0, 0);
        float denominator = n * n - s * s;
        float du = s * MathF.Sqrt(MathF.Max(0, n * n + a * a - s * s)), dv = s * MathF.Sqrt(MathF.Max(0, n * n + b * b - s * s));
        float u0 = (a * n - du) / denominator, u1 = (a * n + du) / denominator;
        float v0 = (b * n - dv) / denominator, v1 = (b * n + dv) / denominator;
        if (u1 < -1 || u0 > 1 || v1 < -1 || v0 > 1) return (1, 1, 0, 0);
        return (Math.Clamp((int)MathF.Floor((u0 + 1) * .5f * size) - 1, 0, size - 1),
            Math.Clamp((int)MathF.Floor((v0 + 1) * .5f * size) - 1, 0, size - 1),
            Math.Clamp((int)MathF.Ceiling((u1 + 1) * .5f * size) + 1, 0, size - 1),
            Math.Clamp((int)MathF.Ceiling((v1 + 1) * .5f * size) + 1, 0, size - 1));
    }
    #endregion
}
