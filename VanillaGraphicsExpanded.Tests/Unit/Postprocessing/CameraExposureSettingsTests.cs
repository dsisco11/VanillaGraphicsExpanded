using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.LumOn;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Checks malformed persisted camera settings cannot inject non-finite metering or adaptation inputs.</summary>
public sealed class CameraExposureSettingsTests
{
    #region Public API
    /// <summary>Display gray follows automatic compensation but remains fixed for directly authored manual camera gain.</summary>
    [Theory]
    [InlineData(true,.18f,1.25f)]
    [InlineData(false,.18f,1.25f)]
    [InlineData(true,.09f,-1f)]
    [InlineData(false,.36f,-1f)]
    public void DisplayPivotUsesCapturedCameraPolicy(bool automatic,float gray,float compensation)
    {
        // Arrange
        var camera=new CameraExposureSettings {Enabled=automatic,MiddleGray=gray,Compensation=compensation}.Snapshot();
        double expected=Math.Log2(gray)+(automatic?compensation:0);

        // Act
        double actual = camera.DisplayLogMiddleGray;

        // Assert
        Assert.InRange(Math.Abs(actual-expected),0,.000001);
    }
    /// <summary>Non-finite persisted numbers recover finite defaults and reversed exposure bounds become a valid interval.</summary>
    [Fact]
    public void InvalidPersistedSettingsProduceCoherentSnapshot()
    {
        // Arrange
        var settings = new CameraExposureSettings
        {
            Compensation = float.NaN, ManualEV100 = float.PositiveInfinity,
            MinEV100 = 7, MaxEV100 = -7, MiddleGray = float.NegativeInfinity,
            LowPercentile = 2, HighPercentile = -1, BrightenRate = 0, DarkenRate = float.NaN
        };

        // Act
        var snapshot = settings.Snapshot();

        // Assert
        Assert.Equal(0, snapshot.Compensation); Assert.Equal(0, snapshot.ManualLogGain);
        Assert.Equal(-7, snapshot.MinLogGain); Assert.Equal(-7, snapshot.MaxLogGain);
        Assert.Equal(.18f, snapshot.MiddleGray);
        Assert.True(snapshot.LowPercentile < snapshot.HighPercentile);
        Assert.InRange(snapshot.LowPercentile, 0, .49f);
        Assert.InRange(snapshot.HighPercentile, .51f, 1);
        Assert.True(snapshot.BrightenRate > 0 && float.IsFinite(snapshot.BrightenRate));
        Assert.Equal(8, snapshot.DarkenRate);
        // Act and assert a separately malformed upper bound retains the default lower bound.
        Assert.Equal(-20, new CameraExposureSettings { MaxEV100 = float.NaN }.Snapshot().MinLogGain);
        // Act: change persisted settings after capturing the immutable snapshot.
        settings.Compensation = 3;

        // Assert the prior snapshot is stable and a new snapshot sees the update.
        Assert.Equal(0, snapshot.Compensation);
        Assert.Equal(3, settings.Snapshot().Compensation);
    }
    /// <summary>The actual opt-in configuration persists nested exposure values and repairs a null settings object.</summary>
    [Fact]
    public void ConfigurationRoundtripPreservesExposureAndSanitizesNull()
    {
        // Arrange
        var config = new VgeConfig { CameraExposure = new() { Enabled = false, Compensation = 1.75f, ManualEV100 = 2, MinEV100 = -8, BrightenRate = 2, DarkenRate = 5 } };

        // Act
        var restored = JsonConvert.DeserializeObject<VgeConfig>(JsonConvert.SerializeObject(config))!;

        // Assert
        Assert.False(restored.CameraExposure.Enabled);
        Assert.Equal(1.75f, restored.CameraExposure.Compensation);
        Assert.Equal(2, restored.CameraExposure.ManualEV100);
        Assert.Equal(-8, restored.CameraExposure.MinEV100);
        Assert.Equal(2, restored.CameraExposure.BrightenRate);
        Assert.Equal(5, restored.CameraExposure.DarkenRate);
        // Act: replace the deserialized nested settings with null and sanitize again.
        restored.CameraExposure = null!;
        restored.Sanitize();

        // Assert sanitization restores the current enabled defaults.
        Assert.NotNull(restored.CameraExposure);
        Assert.True(restored.CameraExposure.Enabled);
    }

    /// <summary>Missing exposure settings use the physical-lighting defaults shared with ConfigLib.</summary>
    [Fact]
    public void MissingExposureSettingsUseCurrentDefaultsWithoutVersionMarker()
    {
        // Arrange the persisted document with no exposure fields or version marker.
        // Act
        var config = JsonConvert.DeserializeObject<VgeConfig>("{}")!;

        // Assert
        Assert.Equal(0, config.CameraExposure.ManualEV100);
        Assert.Equal(-2, config.CameraExposure.MinEV100);
        Assert.Equal(20, config.CameraExposure.MaxEV100);
        Assert.Equal(4, config.CameraExposure.BrightenRate);
        Assert.Equal(8, config.CameraExposure.DarkenRate);
        Assert.Null(JObject.FromObject(config)["PhotometricVersion"]);

        var definition = JObject.Parse(TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
        int bounds = 0;
        foreach (var category in ((JObject)definition["settings"]!).Properties())
        foreach (var setting in ((JObject)category.Value).Properties())
        {
            if (setting.Value["code"]?.Value<string>() == "CameraExposure.MinEV100")
            {
                Assert.Equal(-2, setting.Value["default"]!.Value<float>());
                bounds++;
            }
            if (setting.Value["code"]?.Value<string>() == "CameraExposure.MaxEV100")
            {
                Assert.Equal(20, setting.Value["default"]!.Value<float>());
                bounds++;
            }
            if (setting.Value["code"]?.Value<string>() == "CameraExposure.BrightenRate")
            {
                Assert.Equal(4, setting.Value["default"]!.Value<float>());
                bounds++;
            }
            if (setting.Value["code"]?.Value<string>() == "CameraExposure.DarkenRate")
            {
                Assert.Equal(8, setting.Value["default"]!.Value<float>());
                bounds++;
            }
        }
        Assert.Equal(4, bounds);
    }
    #endregion
}
