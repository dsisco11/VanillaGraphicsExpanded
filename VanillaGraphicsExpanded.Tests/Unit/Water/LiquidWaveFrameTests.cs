using System.Numerics;
using VanillaGraphicsExpanded.PBR.Liquids;

namespace VanillaGraphicsExpanded.Tests.Unit.Water;

/// <summary>Checks camera-independent wave phase at fixed world coordinates.</summary>
public sealed class LiquidWaveFrameTests
{
    #region Public API
    /// <summary>Moving the camera origin leaves the computed wave height at a fixed world point unchanged.</summary>
    [Fact]
    public void FixedWorldPointRetainsWavePhaseAcrossCameraMovement()
    {
        // Arrange snapshots at the same time with different camera origins and choose fixed world-space samples.
        const double seconds = 81346.25;
        const double worldX = 150001.25;
        const double worldZ = -210003.75;
        var first = LiquidWaveFrame.FromState(seconds, 150000, -210000, 0.4f);
        var second = LiquidWaveFrame.FromState(seconds, 150031, -209983, 0.4f);
        Vector4 a = first.Phases;
        Vector4 b = second.Phases;
        (double wavelength, double dx, double dz)[] bands =
        [
            (3, 0.89442719, 0.44721360),
            (5, -0.31622777, 0.94868330),
            (8, 0.70710678, -0.70710678),
            (13, -0.85749293, -0.51449576)
        ];

        // Act and assert each independently directed wavelength band at the same world point.
        for (int i = 0; i < bands.Length; i++)
        {
            var (wavelength, dx, dz) = bands[i];
            double k = 2 * Math.PI / wavelength;
            double firstHeight = Math.Sin(k * ((worldX - 150000) * dx + (worldZ + 210000) * dz) - a[i]);
            double secondHeight = Math.Sin(k * ((worldX - 150031) * dx + (worldZ + 209983) * dz) - b[i]);
            Assert.InRange(Math.Abs(firstHeight - secondHeight), 0, 0.0001);
        }

        // Assert the wind snapshot remains shared across the camera-origin change.
        Assert.Equal(first.Wind, second.Wind);
    }
    #endregion
}
