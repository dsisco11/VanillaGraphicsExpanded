using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Measures whether angular reconstruction removes concentrated Mie interpolation error.</summary>
public sealed class AtmosphereMieTransportTests(ITestOutputHelper output)
{
    #region Angular interpolation
    /// <summary>Separating transport reduces high-sun interpolation error without changing integrated texel radiance.</summary>
    [Fact]
    public void SeparateMieImprovesHighSunInterpolation()
    {
        const int width = 32, height = 24;
        float horizon = AtmosphereSkyMapping.Horizon(.001f);
        Vector3 sun = Vector3.Normalize(new Vector3(.25f, 1, .1f));
        Vector3[] directions = new Vector3[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) directions[y * width + x] = AtmosphereMieTransport.Direction(x, y, width, height, horizon);
        Vector3[] total = new Vector3[directions.Length], mie = new Vector3[directions.Length];
        AtmosphereModel.RadianceBatch(directions, sun, .001f, 1, total, mieTransport: mie);
        Vector3[] background = new Vector3[total.Length];
        for (int i = 0; i < total.Length; i++)
        {
            background[i] = total[i] - mie[i] * AtmosphereMieTransport.Factor(Vector3.Dot(directions[i], sun));
            Assert.InRange(Vector3.Distance(total[i], background[i] + mie[i] * AtmosphereMieTransport.Factor(Vector3.Dot(directions[i], sun))), 0, 1e-5f);
        }
        double originalError = 0, splitError = 0;
        for (int y = 18; y < height - 1; y++)
        for (int x = 0; x < width; x++)
        {
            float row = (y + .5f) / (height - 1);
            float elevation = AtmosphereSkyMapping.Elevation(row, horizon), azimuth = (x + 1f) / width * 2 * MathF.PI;
            Vector3 ray = new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
            Vector3 exact = AtmosphereModel.Radiance(ray, sun, .001f, 1);
            Vector3 old = Midpoint(total, x, y, width);
            Vector3 split = Midpoint(background, x, y, width) + Midpoint(mie, x, y, width) * AtmosphereMieTransport.Factor(Vector3.Dot(ray, sun));
            originalError += Vector3.DistanceSquared(old, exact);
            splitError += Vector3.DistanceSquared(split, exact);
        }
        output.WriteLine($"High-sun squared RGB interpolation error: original={originalError:G8}; split={splitError:G8}; ratio={splitError / originalError:G6}");
        Assert.True(splitError < originalError * .5, $"Split error {splitError} should be substantially below original {originalError}.");
    }

    /// <summary>Samples the center of a four-texel cell, including the periodic azimuth seam.</summary>
    private static Vector3 Midpoint(Vector3[] values, int x, int y, int width)
        => (values[y * width + x] + values[y * width + (x + 1) % width]
            + values[(y + 1) * width + x] + values[(y + 1) * width + (x + 1) % width]) * .25f;
    #endregion
}
