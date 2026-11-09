using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Checks batched transport against the independent scalar integration and passive-medium bounds.</summary>
public sealed class AtmosphereMultipleScatteringTensorTests
{
    #region Public API
    /// <summary>Checks the geometric-series denominator and interpolation after complete table construction.</summary>
    [Theory(Skip = "CPU multiple-scattering numerical validation is disabled.")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TableMatchesScalarAtEveryQuality(int quality)
    {
        // Arrange the quadrature budget selected by each quality row for paired tensor and scalar-reference builds.
        var budget = AtmosphereScatteringBudget.FromQuality(quality);
        // Act by building both tables at the same quadrature settings, then compare interpolated samples and day/night bounds.
        var actual = AtmosphereMultipleScattering.Build(.1f, groundAlbedo: 1, sunSamples: 3, altitudeSamples: 3,
            directionSamples: budget.DirectionSamples, raySamples: budget.RaySamples, lightSamples: budget.LightSamples);
        var expected = AtmosphereMultipleScattering.Build(.1f, groundAlbedo: 1, sunSamples: 3, altitudeSamples: 3,
            directionSamples: budget.DirectionSamples, raySamples: budget.RaySamples, lightSamples: budget.LightSamples, useScalarReference: true);
        // Assert: compare interpolated samples across representative altitudes, solar angles and channels.
        foreach (float altitude in new[] { .001f, 2f, 24.751f, 50f, 99.001f })
        foreach (float cosine in new[] { -1f, -.2f, 0f, .5f, 1f })
            Close(expected.Sample(altitude, cosine), actual.Sample(altitude, cosine));
        Vector3 daytime = actual.Sample(0, 1);
        for (int channel = 0; channel < 3; channel++)
            Assert.True(float.IsFinite(daytime[channel]) && daytime[channel] > 0);
        Assert.True(actual.Sample(0, -1).Length() < 1e-6f);
    }
    #endregion

    #region Private
    /// <summary>Allows floating-point exponential and regrouping error while rejecting visible transport drift.</summary>
    private static void Close(Vector3 expected, Vector3 actual)
    {
        for (int channel = 0; channel < 3; channel++)
            Assert.True(float.IsFinite(actual[channel]) && MathF.Abs(actual[channel] - expected[channel]) <= 1e-5f + 1e-4f * MathF.Abs(expected[channel]),
                $"Expected {expected}, actual {actual}, channel {channel}");
    }
    #endregion
}
