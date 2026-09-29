using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Separates concentrated solar scattering from smoothly interpolated atmospheric transport.</summary>
internal static class AtmosphereMieTransport
{
    #region Angular reconstruction
    /// <summary>Evaluates the normalized angular factor shared by integration and display reconstruction.</summary>
    internal static float Factor(float cosine)
    {
        const float g = .76f;
        return (1 - g * g) / (4 * MathF.PI * MathF.Pow(1 + g * g - 2 * g * Math.Clamp(cosine, -1, 1), 1.5f));
    }

    /// <summary>Reconstructs a texel direction from the production horizon-focused grid.</summary>
    internal static Vector3 Direction(int x, int y, int width, int height, float horizon)
    {
        float elevation = AtmosphereSkyMapping.Elevation(height == 1 ? .5f : (float)y / (height - 1), horizon);
        float azimuth = (x + .5f) / width * (2 * MathF.PI);
        return Vector3.Normalize(new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth)));
    }
    #endregion

    #region Texture layout
    /// <summary>Packs smooth background and unweighted Mie transport into separate elevation bands per distance slice.</summary>
    internal static void Pack(ReadOnlySpan<float> total, ReadOnlySpan<float> mie, Span<float> destination,
        int width, int height, int depth, Vector3 sun, float horizon)
    {
        int sliceLength = checked(width * height * 4);
        if (total.Length != sliceLength * depth || (!mie.IsEmpty && mie.Length != total.Length)
            || destination.Length != total.Length * 2) throw new ArgumentException("Atmospheric transport dimensions differ.");
        // Separate before FP16 conversion, so the narrow angular lobe is never interpolated.
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float factor = Factor(Vector3.Dot(Direction(x, y, width, height, horizon), sun));
            for (int z = 0; z < depth; z++)
            {
                int source = z * sliceLength + (y * width + x) * 4;
                int target = z * sliceLength * 2 + (y * width + x) * 4;
                for (int channel = 0; channel < 3; channel++)
                {
                    float transport = mie.IsEmpty ? 0 : mie[source + channel];
                    destination[target + channel] = MathF.Max(0, total[source + channel] - transport * factor);
                    destination[target + sliceLength + channel] = transport;
                }
                destination[target + 3] = destination[target + sliceLength + 3] = 1;
            }
        }
    }
    #endregion
}
