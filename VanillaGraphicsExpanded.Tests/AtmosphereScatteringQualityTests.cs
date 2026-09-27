using System.Numerics;
using System.Reflection;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies quality admission, medium-table reuse, and bounded multiple-scattering resolution.</summary>
public sealed class AtmosphereScatteringQualityTests
{
    #region Quality and cache ownership
    /// <summary>The default table is independent of sky dimensions and is reused for sun motion.</summary>
    [Fact]
    public void QualitySizesAndReusesMediumTable()
    {
        const int quality = 0;
        var lookup = new AtmosphereLookup();
        Assert.True(lookup.Update(Vector3.UnitY, 2, 0, width: 16, height: 8, quality: quality));
        var table = MediumTable(lookup);
        Assert.Equal(32 * (quality + 1), table.Width);
        Assert.Equal(16 * (quality + 1), table.Height);
        Assert.True(table.Sample(2, .5f).LengthSquared() > 0);
        Assert.False(lookup.Update(Vector3.UnitY, 2, 0, width: 16, height: 8, quality: quality));
        Assert.True(lookup.Update(Vector3.UnitX, 2, 0, width: 16, height: 8, quality: quality));
        Assert.Same(table, MediumTable(lookup));
    }

    /// <summary>A quality edit replaces the cached table even when observer, weather and sky size are identical.</summary>
    [Fact]
    public void QualityOnlyEditRebuildsLighting()
    {
        var lookup = new AtmosphereLookup();
        lookup.Update(Vector3.UnitY, 2, 0, width: 16, height: 8);
        var original = lookup.Current;
        var table = MediumTable(lookup);
        Assert.True(lookup.Update(Vector3.UnitY, 2, 0, width: 16, height: 8, quality: 1));
        Assert.NotSame(original, lookup.Current);
        Assert.NotSame(table, MediumTable(lookup));
        Assert.Equal(64, MediumTable(lookup).Width);
        Assert.Equal(2, lookup.Revision);
    }

    /// <summary>Completed admitted work is published before the latest quality starts its replacement.</summary>
    [Fact]
    public async Task AsyncCompletionAdmitsQualityOnlyChange()
    {
        using var computation = new AtmosphereComputation();
        Assert.Null(computation.Update(Vector3.UnitY, 2, 0, 16, 8));
        var first = await computation.Pending!.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Same(first, computation.Update(Vector3.UnitY, 2, 0, 16, 8, quality: 1));
        var replacement = Assert.IsAssignableFrom<Task<AtmosphereLighting>>(computation.Pending);
        var second = await replacement.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.NotSame(first, second);
        Assert.Same(second, computation.Update(Vector3.UnitY, 2, 0, 16, 8, quality: 1));
        Assert.Null(computation.Pending);
        var lookup = (AtmosphereLookup)typeof(AtmosphereComputation).GetField("lookup", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(computation)!;
        Assert.Equal(64, MediumTable(lookup).Width);
    }

    /// <summary>Reads cache ownership without adding an observation-only production API.</summary>
    private static AtmosphereMultipleScattering MediumTable(AtmosphereLookup lookup) =>
        (AtmosphereMultipleScattering)typeof(AtmosphereLookup).GetField("multipleScattering", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(lookup)!;
    #endregion

    #region Bounds and cancellation
    /// <summary>Quality increases spatial dimensions and quadrature budgets linearly with bounded endpoints.</summary>
    [Theory]
    [InlineData(-1, 32, 16, 128, 24, 12)]
    [InlineData(0, 32, 16, 128, 24, 12)]
    [InlineData(1, 64, 32, 256, 48, 24)]
    [InlineData(2, 96, 48, 384, 72, 36)]
    [InlineData(3, 128, 64, 512, 96, 48)]
    [InlineData(4, 128, 64, 512, 96, 48)]
    public void QualitySelectsCompleteBudget(int quality, int width, int height, int directions, int rays, int light)
    {
        Assert.Equal(new AtmosphereScatteringBudget(width, height, directions, rays, light),
            AtmosphereScatteringBudget.FromQuality(quality));
    }

    /// <summary>Every quality's quadrature executes on a bounded table without incurring maximum full-table startup cost.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void QualityQuadratureProducesFiniteLighting(int quality)
    {
        var budget = AtmosphereScatteringBudget.FromQuality(quality);
        var table = AtmosphereMultipleScattering.Build(1, sunSamples: 2, altitudeSamples: 2,
            directionSamples: budget.DirectionSamples, raySamples: budget.RaySamples, lightSamples: budget.LightSamples);
        Vector3 value = table.Sample(0, 1);
        for (int channel = 0; channel < 3; channel++)
            Assert.True(float.IsFinite(value[channel]) && value[channel] > 0);
        Assert.True(table.Sample(0, -1).Length() < 1e-6f);
    }

    /// <summary>Solar optical-depth refinement affects source radiance without altering isotropic feedback.</summary>
    [Fact]
    public void LightQuadratureRefinesSourceOnly()
    {
        Vector3 direction = Vector3.Normalize(new Vector3(1, .1f, 0));
        Vector3 sun = Vector3.Normalize(new Vector3(1, .03f, 0));
        var coarse = AtmosphereModel.MultipleScatteringTransfer(direction, sun, 2, 1, .1f, 48, 2);
        var fine = AtmosphereModel.MultipleScatteringTransfer(direction, sun, 2, 1, .1f, 48, 96);
        Assert.Equal(coarse.Feedback, fine.Feedback);
        Assert.True(Vector3.Distance(coarse.Source, fine.Source) > 1e-6f);
        for (int channel = 0; channel < 3; channel++)
            Assert.True(float.IsFinite(fine.Source[channel]) && fine.Source[channel] > 0);
    }

    /// <summary>The maximum quality is admitted but cancellation stops integration; larger tables are rejected.</summary>
    [Fact]
    public void MaximumTableHonorsCancellationAndRejectsLargerDimensions()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => AtmosphereMultipleScattering.Build(1, cancellation.Token,
            sunSamples: 128, altitudeSamples: 64, directionSamples: 1024, raySamples: 192, lightSamples: 96));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, sunSamples: 129));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, altitudeSamples: 65));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, lightSamples: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, lightSamples: 97));
    }
    #endregion
}
