using System.Globalization;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Postprocessing.Diagnostics;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Checks exposure snapshot logging with independent camera and local policy inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ExposureSnapshotTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Log fields use invariant numbers and identify missing history without implying valid samples.</summary>
    [Fact]
    public void SnapshotFormattingIsInvariantAndIdentifiesMissingHistory()
    {
        // Arrange: Switch to a comma-decimal culture and supply distinct camera, local, and invalid-history values.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            float[] data = [2, -6, -12, 0, 8, 1, 2, 3, -12, -12, 0, 5];
            // Act: Format without valid history while the non-invariant culture is active.
            string text = ExposureSnapshotLog.Format(data, Camera(), Policy(), false, 17, false, true);
            // Assert: Fields remain invariant, missing samples stay invalid, and the wrong data length is rejected.
            Assert.Contains("frame=17", text);
            Assert.Contains("history=False", text);
            Assert.Contains("cameraEV100=-2.00000", text);
            Assert.Contains("sceneEV100=NaN", text);
            Assert.Contains("requestedEV100=-8.00000", text);
            Assert.Contains("globalStops=2.00000", text);
            Assert.Contains("localStops=3.00000", text);
            Assert.Contains("combinedStops=5.00000", text);
            Assert.Contains("combinedGain=32", text);
            Assert.Contains("meteringValid=0", text);
            Assert.Contains("localProbeValid=0", text);
            Assert.Contains("FXAA=True", text);
            Assert.Contains("LumOn=False", text);
            Assert.DoesNotContain("2,00000", text);
            Assert.Throws<ArgumentException>(() => ExposureSnapshotLog.Format(new float[11], Camera(), Policy(), false, 0, false, false));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    #endregion

    #region Private
/// <summary>Declares the local compression parameters represented by the authored snapshot.</summary>
    private static LocalExposureParameters Policy() => new(true, 1, 1, 1, .5f, .5f, 4, 4, false);

    /// <summary>Declares automatic camera exposure with the authored two-stop brightening cap.</summary>
    private static CameraExposureParameters Camera() => new(true, 0, 0, -20, 2, .18f, 0, 1, 4, 8, false);
    #endregion
}
