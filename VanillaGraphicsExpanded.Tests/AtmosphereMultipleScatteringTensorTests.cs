using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks batched transport against the independent scalar integration and passive-medium bounds.</summary>
public sealed class AtmosphereMultipleScatteringTensorTests
{
    #region Transport parity
    /// <summary>Exercises incomplete vectors and batch tails over ground, horizon, daylight and night paths.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(128)]
    [InlineData(129)]
    public void TransferMatchesScalar(int count)
    {
        Vector3[] directions = AtmosphereTensorTests.Directions(count).Select(Vector3.Normalize).ToArray();
        var sources = new Vector3[count];
        var feedback = new Vector3[count];
        foreach (float altitude in new[] { .001f, 2f, 25f, 99f })
        foreach (float aerosol in new[] { .1f, 1f, 8f })
        foreach (float ground in new[] { 0f, .1f, 1f })
        foreach (Vector3 sun in new[] { Vector3.UnitY, Vector3.Normalize(new Vector3(1, -.03f, 0)), -Vector3.UnitY })
        {
            AtmosphereModel.MultipleScatteringTransferBatch(directions, sun, altitude, aerosol, ground, 24, 12, sources, feedback);
            for (int i = 0; i < count; i++)
            {
                var expected = AtmosphereModel.MultipleScatteringTransfer(directions[i], sun, altitude, aerosol, ground, 24, 12);
                Close(expected.Source, sources[i]);
                Close(expected.Feedback, feedback[i]);
                for (int channel = 0; channel < 3; channel++) Assert.InRange(feedback[i][channel], 0, 1.00001f);
            }
        }
    }

    /// <summary>Checks the geometric-series denominator and interpolation after complete table construction.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TableMatchesScalarAtEveryQuality(int quality)
    {
        var budget = AtmosphereScatteringBudget.FromQuality(quality);
        var actual = AtmosphereMultipleScattering.Build(.1f, groundAlbedo: 1, sunSamples: 3, altitudeSamples: 3,
            directionSamples: budget.DirectionSamples, raySamples: budget.RaySamples, lightSamples: budget.LightSamples);
        var expected = AtmosphereMultipleScattering.Build(.1f, groundAlbedo: 1, sunSamples: 3, altitudeSamples: 3,
            directionSamples: budget.DirectionSamples, raySamples: budget.RaySamples, lightSamples: budget.LightSamples, useScalarReference: true);
        foreach (float altitude in new[] { .001f, 2f, 24.751f, 50f, 99.001f })
        foreach (float cosine in new[] { -1f, -.2f, 0f, .5f, 1f })
            Close(expected.Sample(altitude, cosine), actual.Sample(altitude, cosine));
    }

    /// <summary>Ensures canceled batches do not perform transport work.</summary>
    [Fact]
    public void TransferHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => AtmosphereModel.MultipleScatteringTransferBatch(
            new[] { Vector3.UnitY }, Vector3.UnitY, 2, 1, .1f, 24, 12, new Vector3[1], new Vector3[1], cancellation.Token));
    }
    #endregion

    #region Assertions
    /// <summary>Allows floating-point exponential and regrouping error while rejecting visible transport drift.</summary>
    private static void Close(Vector3 expected, Vector3 actual)
    {
        for (int channel = 0; channel < 3; channel++)
            Assert.True(float.IsFinite(actual[channel]) && MathF.Abs(actual[channel] - expected[channel]) <= 1e-5f + 1e-4f * MathF.Abs(expected[channel]),
                $"Expected {expected}, actual {actual}, channel {channel}");
    }
    #endregion
}
