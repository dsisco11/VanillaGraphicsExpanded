using System;
using System.Numerics;
using System.Threading;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Immutable isotropic multiple-scattering source per unit solar irradiance, indexed by height and solar zenith.</summary>
internal sealed class AtmosphereMultipleScattering
{
    internal const int DefaultWidth = 32;
    internal const int DefaultHeight = 16;
    internal const int DefaultDirectionSamples = 128;
    internal const int DefaultRaySamples = 24;
    internal const int DefaultLightSamples = 12;
    private readonly Vector3[] values;
    private readonly int width, height;

    /// <summary>Solar-angle sample count of this immutable table.</summary>
    internal int Width => width;
    /// <summary>Altitude sample count of this immutable table.</summary>
    internal int Height => height;

    #region Construction
    /// <summary>Takes ownership of a fully computed, private table.</summary>
    private AtmosphereMultipleScattering(Vector3[] values, int width, int height)
    {
        this.values = values; this.width = width; this.height = height;
    }

    /// <summary>Builds the angular-average source and its convergent isotropic scattering series off the render thread.</summary>
    internal static AtmosphereMultipleScattering Build(float aerosol, CancellationToken cancellationToken = default,
        float groundAlbedo = .1f, int sunSamples = DefaultWidth, int altitudeSamples = DefaultHeight,
        int directionSamples = DefaultDirectionSamples, int raySamples = DefaultRaySamples,
        int lightSamples = DefaultLightSamples)
    {
        if (!float.IsFinite(aerosol) || aerosol < .1f || aerosol > 8f) throw new ArgumentOutOfRangeException(nameof(aerosol));
        if (!float.IsFinite(groundAlbedo) || groundAlbedo < 0 || groundAlbedo > 1) throw new ArgumentOutOfRangeException(nameof(groundAlbedo));
        if (sunSamples < 2 || sunSamples > (DefaultWidth * 4)) throw new ArgumentOutOfRangeException(nameof(sunSamples));
        if (altitudeSamples < 2 || altitudeSamples > (DefaultHeight * 4)) throw new ArgumentOutOfRangeException(nameof(altitudeSamples));
        if (directionSamples < 4 || directionSamples > 1024) throw new ArgumentOutOfRangeException(nameof(directionSamples));
        if (raySamples < 2 || raySamples > 256) throw new ArgumentOutOfRangeException(nameof(raySamples));
        if (lightSamples < 2 || lightSamples > (DefaultLightSamples << 3)) throw new ArgumentOutOfRangeException(nameof(lightSamples));
        var values = new Vector3[sunSamples * altitudeSamples];
        for (int y = 0; y < altitudeSamples; y++)
        {
            // Squared height resolves the dense lower atmosphere without enlarging the table.
            float v = (float)y / (altitudeSamples - 1);
            float altitude = .001f + 99f * v * v;
            for (int x = 0; x < sunSamples; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                float cosine = 2f * x / (sunSamples - 1) - 1f;
                Vector3 sun = new(MathF.Sqrt(MathF.Max(0, 1f - cosine * cosine)), cosine, 0);
                Vector3 source = Vector3.Zero, feedback = Vector3.Zero;
                for (int d = 0; d < directionSamples; d++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    float up = 1f - 2f * (d + .5f) / directionSamples;
                    float azimuth = d * 2.39996323f;
                    float horizontal = MathF.Sqrt(1f - up * up);
                    Vector3 direction = new(horizontal * MathF.Cos(azimuth), up, horizontal * MathF.Sin(azimuth));
                    var transfer = AtmosphereModel.MultipleScatteringTransfer(direction, sun, altitude, aerosol, groundAlbedo, raySamples, lightSamples);
                    source += transfer.Source; feedback += transfer.Feedback;
                }
                source /= directionSamples; feedback /= directionSamples;
                // Angular averaging already includes 1/(4pi). The source is incident radiance,
                // not another direct-view contribution: consumers scatter it once more.
                values[y * sunSamples + x] = source / Vector3.Max(new Vector3(1e-5f), Vector3.One - feedback);
            }
        }
        return new(values, sunSamples, altitudeSamples);
    }
    #endregion

    #region Sampling
    /// <summary>Interpolates incident multiple-scattering radiance using the local height and local solar zenith cosine.</summary>
    internal Vector3 Sample(float altitudeKm, float sunCosine)
    {
        float x = (Math.Clamp(sunCosine, -1f, 1f) + 1f) * .5f * (width - 1);
        float y = MathF.Sqrt(Math.Clamp((altitudeKm - .001f) / 99f, 0f, 1f)) * (height - 1);
        int ix = Math.Min((int)x, width - 2), iy = Math.Min((int)y, height - 2);
        return Vector3.Lerp(Vector3.Lerp(values[iy * width + ix], values[iy * width + ix + 1], x - ix),
            Vector3.Lerp(values[(iy + 1) * width + ix], values[(iy + 1) * width + ix + 1], x - ix), y - iy);
    }
    #endregion
}
