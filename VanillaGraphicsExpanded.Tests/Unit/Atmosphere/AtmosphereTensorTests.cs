using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Tests.Support.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Compares batched atmospheric integration with the independent scalar reference.</summary>
public sealed class AtmosphereTensorTests
{

    #region Public API
    /// <summary>Covers batch boundaries, horizon crossings, night, altitude and aerosol extremes.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(31)]
    [InlineData(128)]
    [InlineData(129)]
    public void BatchMatchesScalar(int count)
    {
        // Arrange normalized test directions and output storage for this theory-row batch length.
        Vector3[] directions = AtmosphereTestDirections.Create(count);
        var actual = new Vector3[count];
        // Act and Assert: compare batched results with scalar integration across the selected input grid.
        foreach (float altitude in new[] { 0f, 2f, 25f, 99f })
        foreach (float aerosol in new[] { .1f, 1f, 8f })
        foreach (Vector3 sun in new[] { Vector3.UnitY, new Vector3(1, .001f, 0), new Vector3(1, -.1f, 0), -Vector3.UnitY })
        {
            AtmosphereModel.RadianceBatch(directions, sun * 3f, altitude, aerosol, actual);
            for (int index = 0; index < count; index++)
            {
                Vector3 expected = AtmosphereModel.Radiance(directions[index], sun * 3f, altitude, aerosol);
                for (int channel = 0; channel < 3; channel++)
                {
                    // SIMD exponential rounding may differ; the bound is much tighter than LUT interpolation error.
                    float tolerance = 1e-5f + 1e-4f * MathF.Abs(expected[channel]);
                    Assert.True(float.IsFinite(actual[index][channel]) && MathF.Abs(actual[index][channel] - expected[channel]) <= tolerance,
                        $"count={count}, lane={index}, channel={channel}, altitude={altitude}, aerosol={aerosol}, sun={sun}: expected={expected[channel]:R}, actual={actual[index][channel]:R}, tolerance={tolerance:R}");
                }
            }
        }
    }

    /// <summary>Empty batches perform no writes to a larger destination.</summary>
    [Fact]
    public void EmptyBatchPreservesDestination()
    {
        // Arrange: seed a destination that makes an unintended write observable.
        Vector3[] destination = [new(7, 8, 9)];
        // Act and Assert: submit an empty batch and verify the existing destination remains unchanged.
        AtmosphereModel.RadianceBatch([], Vector3.UnitY, 0, 1, destination);
        Assert.Equal(new Vector3(7, 8, 9), destination[0]);
    }
    #endregion
}
