using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Compares batched atmospheric integration with the independent scalar reference.</summary>
public sealed class AtmosphereTensorTests
{
    #region Numerical agreement
    /// <summary>Covers batch boundaries, horizon crossings, night, altitude and aerosol extremes.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(31)]
    [InlineData(128)]
    [InlineData(129)]
    public void BatchMatchesScalar(int count)
    {
        Vector3[] directions = Directions(count);
        var actual = new Vector3[count];
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
        Vector3[] destination = [new(7, 8, 9)];
        AtmosphereModel.RadianceBatch([], Vector3.UnitY, 0, 1, destination);
        Assert.Equal(new Vector3(7, 8, 9), destination[0]);
    }
    #endregion

    #region Workload construction
    /// <summary>Returns deterministic directions including exact and near horizon samples with non-unit lengths.</summary>
    internal static Vector3[] Directions(int count)
    {
        var result = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float y = (i % 8) switch { 0 => 0, 1 => .0001f, 2 => -.0001f, _ => 2f * (i + .5f) / count - 1f };
            float azimuth = i * 2.39996323f;
            float horizontal = MathF.Sqrt(MathF.Max(0, 1f - y * y));
            result[i] = new Vector3(MathF.Cos(azimuth) * horizontal, y, MathF.Sin(azimuth) * horizontal) * (1f + i % 5);
        }
        return result;
    }
    #endregion
}
