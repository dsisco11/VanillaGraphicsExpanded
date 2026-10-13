using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.LumOn;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        Assert.Equal(8, snapshot.DarkenRate);
        Assert.Equal(2, new CameraExposureSettings { MaxEV = float.NaN }.Snapshot().MaxEV);
        settings.Compensation = 3;
        Assert.Equal(0, snapshot.Compensation);
        Assert.Equal(3, settings.Snapshot().Compensation);
    }
    /// <summary>The actual opt-in configuration persists nested exposure values and repairs a null settings object.</summary>
    [Fact]
    public void ConfigurationRoundtripPreservesExposureAndSanitizesNull()
    {
        var config = new VgeConfig { CameraExposure = new() { Enabled = false, Compensation = 1.75f, ManualEV = -2, MaxEV = 8, BrightenRate = 2, DarkenRate = 5 } };
        var restored = JsonConvert.DeserializeObject<VgeConfig>(JsonConvert.SerializeObject(config))!;
        Assert.False(restored.CameraExposure.Enabled);
        Assert.Equal(1.75f, restored.CameraExposure.Compensation);
        Assert.Equal(-2, restored.CameraExposure.ManualEV);
        Assert.Equal(8, restored.CameraExposure.MaxEV);
        Assert.Equal(2, restored.CameraExposure.BrightenRate);
        Assert.Equal(5, restored.CameraExposure.DarkenRate);
        restored.CameraExposure = null!;
        restored.Sanitize();
        Assert.NotNull(restored.CameraExposure);
        Assert.True(restored.CameraExposure.Enabled);
    }

    /// <summary>Missing exposure settings use the physical-lighting defaults shared with ConfigLib.</summary>
    [Fact]
    public void MissingExposureSettingsUseCurrentDefaultsWithoutVersionMarker()
    {
        var config = JsonConvert.DeserializeObject<VgeConfig>("{}")!;
        Assert.Equal(0, config.CameraExposure.ManualEV);
        Assert.Equal(-20, config.CameraExposure.MinEV);
        Assert.Equal(2, config.CameraExposure.MaxEV);
        Assert.Equal(4, config.CameraExposure.BrightenRate);
        Assert.Equal(8, config.CameraExposure.DarkenRate);
        Assert.Null(JObject.FromObject(config)["PhotometricVersion"]);

        var definition = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
        int bounds = 0;
        foreach (var category in ((JObject)definition["settings"]!).Properties())
        foreach (var setting in ((JObject)category.Value).Properties())
        {
            if (setting.Value["code"]?.Value<string>() == "CameraExposure.MinEV")
            {
                Assert.Equal(-20, setting.Value["default"]!.Value<float>());
                bounds++;
            }
            if (setting.Value["code"]?.Value<string>() == "CameraExposure.MaxEV")
            {
                Assert.Equal(2, setting.Value["default"]!.Value<float>());
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
