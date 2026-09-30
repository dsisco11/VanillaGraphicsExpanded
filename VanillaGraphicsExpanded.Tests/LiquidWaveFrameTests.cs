using System.Numerics;
using VanillaGraphicsExpanded.PBR.Liquids;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks that the shared wave snapshot remains anchored when the camera moves.</summary>
public sealed class LiquidWaveFrameTests
{
    #region World anchoring
    /// <summary>Changing camera origin does not change wave phase at a fixed world point.</summary>
    [Fact]
    public void FixedWorldPointRetainsWavePhaseAcrossCameraMovement()
    {
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
        for (int i = 0; i < bands.Length; i++)
        {
            var (wavelength, dx, dz) = bands[i];
            double k = 2 * Math.PI / wavelength;
            double firstHeight = Math.Sin(k * ((worldX - 150000) * dx + (worldZ + 210000) * dz) - a[i]);
            double secondHeight = Math.Sin(k * ((worldX - 150031) * dx + (worldZ + 209983) * dz) - b[i]);
            Assert.InRange(Math.Abs(firstHeight - secondHeight), 0, 0.0001);
        }
        Assert.Equal(first.Wind, second.Wind);
    }
    #endregion
}
