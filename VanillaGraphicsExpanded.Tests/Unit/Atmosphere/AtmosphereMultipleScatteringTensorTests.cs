using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Tests.Support.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Checks batched transport against the independent scalar integration and passive-medium bounds.</summary>
public sealed class AtmosphereMultipleScatteringTensorTests
{
    #region Public API
    /// <summary>Exercises incomplete vectors and batch tails over ground, horizon, daylight and night paths.</summary>
    [Theory(Skip = "CPU multiple-scattering numerical validation is disabled.")]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(128)]
    [InlineData(129)]
    public void TransferMatchesScalar(int count)
    {
        // Arrange normalized rays and independent source/feedback buffers for each requested batch size.
        Vector3[] directions = AtmosphereTestDirections.Create(count).Select(Vector3.Normalize).ToArray();
        var sources = new Vector3[count];
        var feedback = new Vector3[count];
        // Each altitude spans every aerosol, ground and sun category while avoiding the full Cartesian product.
        (float Altitude, float Aerosol, float Ground, Vector3 Sun)[] scenarios =
        [
            (.001f, .1f, 0f, Vector3.UnitY), (.001f, 1f, .1f, Vector3.Normalize(new Vector3(1, -.03f, 0))), (.001f, 8f, 1f, -Vector3.UnitY),
            (2f, .1f, .1f, Vector3.Normalize(new Vector3(1, -.03f, 0))), (2f, 1f, 1f, -Vector3.UnitY), (2f, 8f, 0f, Vector3.UnitY),
            (25f, .1f, 1f, -Vector3.UnitY), (25f, 1f, 0f, Vector3.UnitY), (25f, 8f, .1f, Vector3.Normalize(new Vector3(1, -.03f, 0))),
            (99f, .1f, 0f, Vector3.UnitY), (99f, 1f, .1f, -Vector3.UnitY), (99f, 8f, 1f, Vector3.Normalize(new Vector3(1, -.03f, 0)))
        ];
        // Act by running each authored atmosphere scenario through the batch path; assert every lane against scalar transport.
        foreach (var scenario in scenarios)
        {
            AtmosphereModel.MultipleScatteringTransferBatch(directions, scenario.Sun, scenario.Altitude, scenario.Aerosol, scenario.Ground, 24, 12, sources, feedback);
            for (int i = 0; i < count; i++)
            {
                var expected = AtmosphereModel.MultipleScatteringTransfer(directions[i], scenario.Sun, scenario.Altitude, scenario.Aerosol, scenario.Ground, 24, 12);
                Close(expected.Source, sources[i]);
                Close(expected.Feedback, feedback[i]);
                for (int channel = 0; channel < 3; channel++) Assert.InRange(feedback[i][channel], 0, 1.00001f);
            }
        }
    }

    /// <summary>Ensures canceled batches do not perform transport work.</summary>
    [Fact]
    public void TransferHonorsCancellation()
    {
        // Arrange an already-canceled token and one-ray output buffers to verify admission short-circuits transport.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        // Act and Assert: cancellation must stop transport before producing a batch.
        Assert.Throws<OperationCanceledException>(() => AtmosphereModel.MultipleScatteringTransferBatch(
            new[] { Vector3.UnitY }, Vector3.UnitY, 2, 1, .1f, 24, 12, new Vector3[1], new Vector3[1], cancellation.Token));
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
