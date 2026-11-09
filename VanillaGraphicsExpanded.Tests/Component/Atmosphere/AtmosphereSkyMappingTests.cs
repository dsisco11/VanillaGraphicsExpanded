using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Checks horizon coordinates, angular integration and matched-budget sky reconstruction.</summary>
public sealed class AtmosphereSkyMappingTests(ITestOutputHelper output)
{
    #region Public API
    /// <summary>Compares identical vertical sample budgets against direct integration around the planetary limb.</summary>
    [Theory]
    [InlineData(.001f, .05f)]
    [InlineData(.001f, -.05f)]
    [InlineData(25f, .05f)]
    [InlineData(99f, -.05f)]
    public void HorizonReconstructionImprovesAtMatchedBudget(float altitude, float sunY)
    {
        // Arrange the sampled altitude/sun-angle pair and a shared 24-row budget for both mappings.
        const int height = 24;
        // Act by mapping matched-budget directions and compare each interpolation with direct transport.
        float horizon = AtmosphereSkyMapping.Horizon(altitude);
        Vector3 sun = Vector3.Normalize(new(1, sunY, 0));
        double oldError = 0, newError = 0;
        // Keep azimuth aligned with a production column so this measures vertical mapping alone.
        foreach (float azimuth in new[] { MathF.PI / 32, MathF.PI * .5f + MathF.PI / 32, MathF.PI + MathF.PI / 32 })
        {
            var oldDirections = new Vector3[height];
            var newDirections = new Vector3[height];
            var oldValues = new Vector3[height];
            var newValues = new Vector3[height];
            for (int y = 0; y < height; y++)
            {
                oldDirections[y] = Direction(((y + .5f) / height - .5f) * MathF.PI, azimuth);
                newDirections[y] = Direction(AtmosphereSkyMapping.Elevation((float)y / (height - 1), horizon), azimuth);
            }
            AtmosphereModel.RadianceBatch(oldDirections, sun, altitude, 1, oldValues);
            AtmosphereModel.RadianceBatch(newDirections, sun, altitude, 1, newValues);
            for (int i = 0; i < 101; i++)
            {
                float elevation = horizon + (i / 100f - .5f) * .12f;
                Vector3 reference = AtmosphereModel.Radiance(Direction(elevation, azimuth), sun, altitude, 1);
                oldError += Vector3.DistanceSquared(reference, Interpolate(oldValues, (elevation / MathF.PI + .5f) * height - .5f));
                newError += Vector3.DistanceSquared(reference, Interpolate(newValues, AtmosphereSkyMapping.Coordinate(elevation, horizon) * (height - 1)));
            }
        }
        output.WriteLine($"altitude={altitude} sunY={sunY}: uniform RMSE={Math.Sqrt(oldError / 303):G8}, horizon RMSE={Math.Sqrt(newError / 303):G8}");
        // Assert the horizon-focused reconstruction has lower squared error than uniform elevation mapping.
        Assert.True(newError < oldError, $"Focused squared error {newError} must improve uniform {oldError}.");
    }
    #endregion

    #region Private
    /// <summary>Constructs a unit spherical direction without altering the transport model.</summary>
    private static Vector3 Direction(float elevation, float azimuth) => new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));

    /// <summary>Reproduces linear texture filtering with clamped vertical addressing.</summary>
    private static Vector3 Interpolate(Vector3[] values, float row)
    {
        row = Math.Clamp(row, 0, values.Length - 1);
        int low = Math.Min((int)row, values.Length - 2);
        return Vector3.Lerp(values[low], values[low + 1], row - low);
    }
    #endregion
}
