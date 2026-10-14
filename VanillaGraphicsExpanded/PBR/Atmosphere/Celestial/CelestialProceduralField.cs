using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Defines the deterministic fictional galaxy field mirrored by the generation shader.</summary>
internal static class CelestialProceduralField
{
    internal const uint Seed = 0x56474531;
    internal const int Version = 1;
    internal static readonly Vector3 Luma = new(.2126f, .7152f, .0722f);
    internal static readonly Vector3 PaletteMaximum = new(1.424f, 1.052f, 1.456f);

    #region Public API
    /// <summary>Avalanches an unsigned key with wrapping arithmetic; CPU and GLSL use identical operations.</summary>
    internal static uint Hash(uint key)
    {
        unchecked { key ^= key >> 16; key *= 0x7feb352d; key ^= key >> 15; key *= 0x846ca68b; return key ^ (key >> 16); }
    }

    /// <summary>Returns independent 24-bit hash streams, never a world or frame random value.</summary>
    internal static float Random(uint id, uint purpose) => (Hash(Seed ^ (uint)Version ^ Hash(id) ^ Hash(purpose)) >> 8) * (1f / 16777216);

    /// <summary>Interpolates a bounded linear color and normalizes its photometric luminance to one.</summary>
    internal static Vector3 Color(float value)
    {
        Vector3 color = value < .5f ? Vector3.Lerp(new(1, .65f, .35f), Vector3.One, value * 2)
            : Vector3.Lerp(Vector3.One, new(.55f, .70f, 1), value * 2 - 1);
        return color / Vector3.Dot(color, Luma);
    }

    /// <summary>Evaluates positive band, bulge and bounded footprint-filtered dust without imagery or display transfer.</summary>
    internal static Vector3 Evaluate(Vector3 direction, int octaves, float footprint)
    {
        float c = .5f, s = .8660254038f;
        Vector3 g = new(direction.X, c * direction.Y + s * direction.Z, -s * direction.Y + c * direction.Z);
        float latitude = MathF.Asin(Math.Clamp(g.Y, -1, 1)), longitude = MathF.Atan2(g.Z, g.X);
        float band = MathF.Exp(-.5f * MathF.Pow(latitude / (.2094395102f), 2));
        float bulge = MathF.Exp(-.5f * (MathF.Pow(latitude / .1396263402f, 2) + MathF.Pow(longitude / .436332313f, 2)));
        float noise = 0, weight = 0, amplitude = 1, frequency = 4;
        for (int i = 0; i < octaves; i++)
        {
            float filtered = Math.Clamp(2 - frequency * footprint, 0, 1);
            noise += amplitude * (filtered * Noise(g * frequency) + (1 - filtered) * .5f);
            weight += amplitude; amplitude *= .5f; frequency *= 2;
        }
        noise /= weight;
        float w = .05f + (band + 2 * bulge) * MathF.Exp(-2 * noise) * (.5f + noise);
        return Color(.25f + .5f * noise) * w;
    }
    #endregion

    #region Private
    /// <summary>Interpolates a continuous direction-space lattice with a smooth first derivative.</summary>
    private static float Noise(Vector3 p)
    {
        int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);
        Vector3 f = p - new Vector3(x, y, z); f = f * f * (new Vector3(3) - 2 * f);
        float result = 0;
        for (int dz = 0; dz < 2; dz++) for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
        {
            uint key;
            unchecked { key = (uint)(x + dx) * 0x8da6b343 ^ (uint)(y + dy) * 0xd8163841 ^ (uint)(z + dz) * 0xcb1ab31f ^ Seed; }
            result += (Hash(key) >> 8) * (1f / 16777216) * (dx == 0 ? 1 - f.X : f.X)
                * (dy == 0 ? 1 - f.Y : f.Y) * (dz == 0 ? 1 - f.Z : f.Z);
        }
        return result;
    }
    #endregion
}
