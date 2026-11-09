using System.Numerics;
using System.Threading;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Verifies quality admission and bounded multiple-scattering resolution.</summary>
public sealed class AtmosphereQualityPolicyTests
{

    #region Public API
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
        // Arrange: use each requested quality and expected bounded dimensions/quadrature tuple from the theory row.
        // Act and Assert: perform the operation and check its observable result.
        Assert.Equal(new AtmosphereScatteringBudget(width, height, directions, rays, light),
            AtmosphereScatteringBudget.FromQuality(quality));
    }

    /// <summary>The maximum quality is admitted but cancellation stops integration; larger tables are rejected.</summary>
    [Fact]
    public void MaximumTableHonorsCancellationAndRejectsLargerDimensions()
    {
        // Arrange an already-canceled build and dimensions just beyond each supported table limit.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        // Act and Assert: cancellation is honored and dimensions outside the table bounds are rejected.
        Assert.Throws<OperationCanceledException>(() => AtmosphereMultipleScattering.Build(1, cancellation.Token,
            sunSamples: 128, altitudeSamples: 64, directionSamples: 1024, raySamples: 192, lightSamples: 96));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, sunSamples: 129));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, altitudeSamples: 65));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, lightSamples: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, lightSamples: 97));
    }
    #endregion
}
