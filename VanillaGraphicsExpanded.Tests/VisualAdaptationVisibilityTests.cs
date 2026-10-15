using System.Numerics;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Compares the selected display response to an independent steady-state threshold model without claiming human acceptance.</summary>
public sealed class VisualAdaptationVisibilityTests(ITestOutputHelper output)
{
    #region Public API
    /// <summary>Confirms published asymptotes, decimal logarithms and threshold-matching normalization, not a fitted production curve.</summary>
    [Theory]
    [InlineData(.000001)]
    [InlineData(.001)]
    [InlineData(.04)]
    [InlineData(10)]
    [InlineData(1000)]
    public void ThresholdMatchingMapsOneSceneThresholdToOneDisplayThreshold(double adaptation)
    {
        const double displayAdaptation = 5;
        double cone = VisualThresholdReference.ConeThreshold(adaptation);
        Assert.True(double.IsFinite(cone) && cone > 0);
        Assert.InRange(Math.Abs(cone * VisualThresholdReference.MatchingGain(adaptation, displayAdaptation)
            - VisualThresholdReference.ConeThreshold(displayAdaptation)), 0, 1e-12);
        if (adaptation <= .001) Assert.Equal(Math.Pow(10, -.72), cone);
        if (adaptation >= 1000) Assert.InRange(Math.Abs(cone / adaptation - Math.Pow(10, -1.255)), 0, 1e-12);
        if (adaptation <= 10)
        {
            double rod = VisualThresholdReference.RodThreshold(adaptation);
            Assert.InRange(Math.Abs(rod * VisualThresholdReference.MatchingGain(adaptation, displayAdaptation, true)
                - VisualThresholdReference.ConeThreshold(displayAdaptation)), 0, 1e-12);
            if (adaptation <= .000001) Assert.Equal(Math.Pow(10, -2.86), rod);
        }
    }

    /// <summary>Independent uniform-scene detail diagnostics expose the unchanged exposure cap and filmic response over dim and bright conditions.</summary>
    [Theory]
    [InlineData(.000001)]
    [InlineData(.001)]
    [InlineData(.04)]
    [InlineData(10)]
    [InlineData(1000)]
    public void UniformSceneReceivesBoundedDisplayRelativeCompression(double adaptation)
    {
        // Assumptions are evaluation choices, not measurements of the user's monitor: 100/0.1 cd/m², dim neutral surround,
        // and steady display adaptation at 5 cd/m². Stimulus geometry/timing and physiological dynamics are not simulated.
        double exposure = Math.Clamp(Math.Log2(.18 / adaptation), -20, 2);
        Vector3 background = new((float)adaptation), detail = background * 1.1f;
        double global = Difference(background, detail, exposure, 0, new(1, 1, 1, 0));
        double baseLog = Math.Log2(adaptation);
        double localStops = Math.Clamp(.5 * (Math.Log2(.18)-exposure-baseLog), -4, 4);
        double local = Difference(background, detail, exposure, localStops, new(1, 1, 1, 0));
        Assert.InRange(localStops,-4,4);
        if(exposure>-20&&exposure<2) Assert.InRange(Math.Abs(localStops),0,1e-12);
        Report($"uniform {adaptation:R}", adaptation, .1 * adaptation, global, local);
    }

    /// <summary>Captured physical inputs distinguish actual global adaptation from a balanced synthetic scene and record threshold tradeoffs.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void CapturedNightInputsReportVisibilityWithoutRetuningTheSelectedCurve(double exposure)
    {
        Vector3[] sources = [new(.00006592794125f, .0000526507864f, .00001556631946f), new(.03469f, .02133f, .003867f)];
        const double adaptedLog = -12.7348446;
        Vector4 capturedControls = new(1, 1, 1.111798286f, .143282309f);
        foreach (Vector3 source in sources)
        {
            double luminance = Luminance(source);
            // A matched broad-region base preserves the 10% detail residual; compression is bounded to the selected four stops.
            double stops = Math.Clamp(.5 * (Math.Log2(.18)-exposure-Math.Log2(luminance)), -4, 4);
            double global = Difference(source, source * 1.1f, exposure, 0, capturedControls);
            double local = Difference(source, source * 1.1f, exposure, stops, capturedControls);
            Assert.InRange(stops, -4, 4);
            Assert.True(double.IsFinite(global) && double.IsFinite(local));
            Report($"captured Y={luminance:R} EV={exposure:R} localStops={stops:R}", Math.Pow(2, adaptedLog), .1 * luminance, global, local);
        }
    }

    /// <summary>Invalid physical adaptation is rejected, and assumed black/white endpoints are interpreted in linear display light.</summary>
    [Fact]
    public void InvalidAdaptationCannotBeInventedAndDisplayEndpointsAreExplicit()
    {
        foreach (double value in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => VisualThresholdReference.ConeThreshold(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualThresholdReference.RodThreshold(100));
        Assert.Equal(.1, VisualThresholdReference.DisplayLuminance(0, 0, 0));
        Assert.InRange(VisualThresholdReference.DisplayLuminance(1, 1, 1), 99.999999, 100.000001);
    }
    #endregion

    #region Private
    /// <summary>Evaluates existing LUT interpolation and RGB8 quantization independently, then computes emitted luminance difference.</summary>
    private static double Difference(Vector3 background, Vector3 detail, double exposure, double localStops, Vector4 controls)
    {
        Vector3[] values = [background, detail];
        double[] luminances = new double[2];
        for (int i = 0; i < 2; i++)
        {
            Vector3 display = FilmicGradingReference.Lookup(values[i] * (float)Math.Pow(2, exposure + localStops), controls);
            luminances[i] = VisualThresholdReference.DisplayLuminance(FilmicGradingReference.Quantize(display.X),
                FilmicGradingReference.Quantize(display.Y), FilmicGradingReference.Quantize(display.Z));
        }
        return Math.Abs(luminances[1] - luminances[0]);
    }

    /// <summary>Reports model-relative detail detectability; rod values use photopic Y only as a declared achromatic sensitivity proxy.</summary>
    private void Report(string name, double adaptation, double increment, double globalDifference, double localDifference)
    {
        double displayThreshold = VisualThresholdReference.ConeThreshold(5);
        double cone = increment / VisualThresholdReference.ConeThreshold(adaptation);
        double rodProxy = adaptation <= 10 ? increment / VisualThresholdReference.RodThreshold(adaptation) : double.NaN;
        output.WriteLine($"{name}: scene cone threshold units={cone:R}; achromatic rod proxy units={rodProxy:R}; "
            + $"global display threshold units={globalDifference / displayThreshold:R}; local={localDifference / displayThreshold:R}. "
            + "Below one means below this model threshold, not an observed user result.");
    }

    /// <summary>Uses photopic Rec.709 Y, which cannot reconstruct a source's scotopic spectral luminance.</summary>
    private static double Luminance(Vector3 color) => .2126 * color.X + .7152 * color.Y + .0722 * color.Z;
    #endregion
}
