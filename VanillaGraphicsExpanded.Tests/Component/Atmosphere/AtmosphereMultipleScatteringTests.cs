using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Tests.Component.Atmosphere.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Validates the bounded isotropic multiple-scattering approximation and its production consumers.</summary>
public sealed class AtmosphereMultipleScatteringTests : IClassFixture<AtmosphereMultipleScatteringTableFixture>
{
    #region Public API
    /// <summary>Uses one bounded immutable-table fixture across the numerical theory rows.</summary>
    public AtmosphereMultipleScatteringTests(AtmosphereMultipleScatteringTableFixture tables)
    {
        this.tables = tables;
    }
    /// <summary>Extreme aerosol and altitude inputs produce finite nonnegative sources, including twilight and night.</summary>
    [Theory(Skip = "CPU multiple-scattering numerical validation is disabled.")]
    [InlineData(.1f)]
    [InlineData(1f)]
    [InlineData(8f)]
    public void SourceRemainsFiniteAndNightIsDark(float aerosol)
    {
        // Arrange: obtain the class-owned default table for this aerosol input.
        var table = tables.GetDefault(aerosol);
        // Assert: confirm the class fixture returns the same immutable table for repeated requests.
        Assert.Same(table, tables.GetDefault(aerosol));

        // Act and Assert: sample the altitude and solar-angle matrix and verify finite nonnegative channels, daylight energy, and night darkness.
        foreach (float altitude in new[] { 0f, 2f, 25f, 99f })
        foreach (float cosine in new[] { -1f, -.1f, 0f, .1f, 1f })
        {
            Vector3 value = table.Sample(altitude, cosine);
            for (int channel = 0; channel < 3; channel++)
                Assert.True(float.IsFinite(value[channel]) && value[channel] >= 0, $"aerosol={aerosol}, altitude={altitude}, cosine={cosine}: {value}");
        }
        Assert.True(table.Sample(0, 1).LengthSquared() > 0);
        Assert.True(table.Sample(0, -1).Length() < 1e-6f);
    }

    /// <summary>Reflective ground contributes additional energy without changing atmospheric optical coefficients.</summary>
    [Fact(Skip = "CPU multiple-scattering numerical validation is disabled.")]
    public void GroundReflectionIncreasesSource()
    {
        // Arrange: prepare black and reflective ground tables to isolate ground-return contribution.
        // Act by building tables with black and reflective ground, then compare the same sample point.
        var black = AtmosphereMultipleScattering.Build(1, groundAlbedo: 0);
        var reflective = AtmosphereMultipleScattering.Build(1, groundAlbedo: .3f);
        Vector3 low = black.Sample(2, .5f), high = reflective.Sample(2, .5f);
        // Assert reflective ground contributes in each radiance channel.
        for (int channel = 0; channel < 3; channel++) Assert.True(high[channel] > low[channel]);
    }

    /// <summary>Higher angular and spatial quadrature bounds the approximation against a refined integration.</summary>
    [Theory(Skip = "CPU multiple-scattering numerical validation is disabled.")]
    [InlineData(1f)]
    public void DefaultQuadratureAgreesWithRefinement(float aerosol)
    {
        // Arrange: reuse the same standard table; retain a fresh independent refined integration.
        var standard = tables.GetDefault(aerosol);
        // Assert: confirm repeated fixture access reuses the standard table.
        Assert.Same(standard, tables.GetDefault(aerosol));
        // Act: construct an independent refined table for comparison.
        var refined = AtmosphereMultipleScattering.Build(aerosol, directionSamples: 256, raySamples: 96);

        // Assert: compare standard and refined values across altitude, solar angle, and channels.
        foreach (float altitude in new[] { 0f, 2f, 25f, 99f })
        foreach (float cosine in new[] { -.1f, 0f, .25f, 1f })
        {
            Vector3 actual = standard.Sample(altitude, cosine), expected = refined.Sample(altitude, cosine);
            for (int channel = 0; channel < 3; channel++)
                Assert.True(MathF.Abs(actual[channel] - expected[channel]) <= 1e-4f + .15f * expected[channel],
                    $"altitude={altitude}, cosine={cosine}, channel={channel}, standard={actual[channel]}, refined={expected[channel]}");
        }
    }

    /// <summary>Sub-bucket weather jitter uses the same physical medium when a changed sun requires a fresh snapshot.</summary>
    [Fact]
    public void WeatherBucketUsesCoherentMedium()
    {
        // Arrange a lookup whose second sun input changes while cloud jitter remains within one weather bucket.
        var lookup = new AtmosphereLookup();
        // Act by publishing the initial medium and then the changed sun with sub-bucket cloud variation.
        lookup.Update(Vector3.UnitY, 2, .5f);
        Vector3 sun = Vector3.Normalize(new Vector3(1, 2, 0));
        lookup.Update(sun, 2, .501f);
        var canonical = new AtmosphereLookup();
        canonical.Update(sun, 2, .5f);
        // Assert the publication matches a canonical lookup at the same quantized medium and new sun.
        Assert.Equal(canonical.Current!.Sky.ToArray(), lookup.Current!.Sky.ToArray());
        Assert.Equal(AtmosphereModel.SolarIrradiance(sun, 2, 4.5f), lookup.Current.Solar);
        Assert.Equal(canonical.Current.Environment, lookup.Current.Environment);
    }

    /// <summary>The same additional sky radiance reaches the published environment and horizon without brightening direct solar irradiance.</summary>
    [Fact]
    public void PublishedLightingIncludesMultipleScattering()
    {
        // Arrange an empty lookup for comparing its published lighting with single-scattering transport.
        var lookup = new AtmosphereLookup();
        // Act by building one snapshot; assert its sky-integrated environment and horizon include added scattering.
        Assert.True(lookup.Update(Vector3.UnitY, 2, 0));
        AtmosphereLighting lighting = lookup.Current!;
        Vector3 environment = Vector3.Zero, horizon = Vector3.Zero;
        bool increased = false;
        for (int y = 0; y < lighting.Height; y++)
        for (int x = 0; x < lighting.Width; x++)
        {
            float elevation = ((y + .5f) / lighting.Height - .5f) * MathF.PI;
            float azimuth = (x + .5f) / lighting.Width * 2f * MathF.PI;
            Vector3 direction = new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
            Vector3 single = AtmosphereModel.Radiance(direction, Vector3.UnitY, 2, 1);
            int offset = (y * lighting.Width + x) * 4;
            Vector3 actual = new(lighting.Sky[offset], lighting.Sky[offset + 1], lighting.Sky[offset + 2]);
            if (direction.Y > 0)
            {
                environment += single * (direction.Y * MathF.Cos(elevation) * 2f * MathF.PI / (lighting.Width * lighting.Height));
                increased |= actual.Length() > single.Length() * 1.01f;
            }
            if (y == lighting.Height / 2) horizon += single / lighting.Width;
        }
        Assert.True(increased);
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.True(lighting.Environment[channel] > environment[channel]);
            Assert.True(lighting.Horizon[channel] > horizon[channel]);
        }
        Assert.Equal(AtmosphereModel.SolarIrradiance(Vector3.UnitY, 2, 1), lighting.Solar);
        Assert.Equal(AtmosphereModel.LocalExtinction(2, 1), lighting.Extinction);
    }
    #endregion

    #region Private
    private readonly AtmosphereMultipleScatteringTableFixture tables;
    #endregion
}
