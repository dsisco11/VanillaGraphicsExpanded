using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.LumOn;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks malformed persisted camera settings cannot inject non-finite metering or adaptation inputs.</summary>
public sealed class CameraExposureSettingsTests
{
    #region Public API
    /// <summary>Non-finite persisted numbers recover finite defaults and reversed exposure bounds become a valid interval.</summary>
    [Fact]
    public void InvalidPersistedSettingsProduceCoherentSnapshot()
    {
        var settings = new CameraExposureSettings
        {
            Compensation = float.NaN, ManualEV = float.PositiveInfinity,
            MinEV = 7, MaxEV = -7, MiddleGray = float.NegativeInfinity,
            LowPercentile = 2, HighPercentile = -1, BrightenRate = 0, DarkenRate = float.NaN
        };
        var snapshot = settings.Snapshot();
        Assert.Equal(0, snapshot.Compensation); Assert.Equal(0, snapshot.ManualEV);
        Assert.Equal(7, snapshot.MinEV); Assert.Equal(7, snapshot.MaxEV);
        Assert.Equal(.18f, snapshot.MiddleGray);
        Assert.True(snapshot.LowPercentile < snapshot.HighPercentile);
        Assert.InRange(snapshot.LowPercentile, 0, .49f);
        Assert.InRange(snapshot.HighPercentile, .51f, 1);
        Assert.True(snapshot.BrightenRate > 0 && float.IsFinite(snapshot.BrightenRate));
        Assert.True(snapshot.DarkenRate > 0 && float.IsFinite(snapshot.DarkenRate));
        settings.Compensation = 3;
        Assert.Equal(0, snapshot.Compensation);
        Assert.Equal(3, settings.Snapshot().Compensation);
    }
    /// <summary>The actual opt-in configuration persists nested exposure values and repairs a null settings object.</summary>
    [Fact]
    public void ConfigurationRoundtripPreservesExposureAndSanitizesNull()
    {
        var config = new VgeConfig { CameraExposure = new() { Enabled = false, Compensation = 1.75f, ManualEV = -2 } };
        var restored = JsonConvert.DeserializeObject<VgeConfig>(JsonConvert.SerializeObject(config))!;
        Assert.False(restored.CameraExposure.Enabled);
        Assert.Equal(1.75f, restored.CameraExposure.Compensation);
        Assert.Equal(-2, restored.CameraExposure.ManualEV);
        restored.CameraExposure = null!;
        restored.Sanitize();
        Assert.NotNull(restored.CameraExposure);
        Assert.True(restored.CameraExposure.Enabled);
    }
    #endregion
}
