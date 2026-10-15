using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.LumOn;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks malformed persisted camera settings cannot inject non-finite metering or adaptation inputs.</summary>
public sealed class CameraExposureSettingsTests
{
    /// <summary>Display gray follows automatic compensation but remains fixed for directly authored manual camera gain.</summary>
    [Theory]
    [InlineData(true,.18f,1.25f)]
    [InlineData(false,.18f,1.25f)]
    [InlineData(true,.09f,-1f)]
    [InlineData(false,.36f,-1f)]
    public void DisplayPivotUsesCapturedCameraPolicy(bool automatic,float gray,float compensation)
    {
        var camera=new CameraExposureSettings {Enabled=automatic,MiddleGray=gray,Compensation=compensation}.Snapshot();
        double expected=Math.Log2(gray)+(automatic?compensation:0);
        Assert.InRange(Math.Abs(camera.DisplayLogMiddleGray-expected),0,.000001);
    }
    #region Public API
    /// <summary>Non-finite persisted numbers recover finite defaults and reversed exposure bounds become a valid interval.</summary>
    [Fact]
    public void InvalidPersistedSettingsProduceCoherentSnapshot()
    {
        var settings = new CameraExposureSettings
        {
            Compensation = float.NaN, ManualEV100 = float.PositiveInfinity,
            MinEV100 = 7, MaxEV100 = -7, MiddleGray = float.NegativeInfinity,
            LowPercentile = 2, HighPercentile = -1, BrightenRate = 0, DarkenRate = float.NaN
        };
        var snapshot = settings.Snapshot();
        Assert.Equal(0, snapshot.Compensation); Assert.Equal(0, snapshot.ManualLogGain);
        Assert.Equal(-7, snapshot.MinLogGain); Assert.Equal(-7, snapshot.MaxLogGain);
        Assert.Equal(.18f, snapshot.MiddleGray);
        Assert.True(snapshot.LowPercentile < snapshot.HighPercentile);
        Assert.InRange(snapshot.LowPercentile, 0, .49f);
        Assert.InRange(snapshot.HighPercentile, .51f, 1);
        Assert.True(snapshot.BrightenRate > 0 && float.IsFinite(snapshot.BrightenRate));
        Assert.Equal(8, snapshot.DarkenRate);
        Assert.Equal(-20, new CameraExposureSettings { MaxEV100 = float.NaN }.Snapshot().MinLogGain);
        settings.Compensation = 3;
        Assert.Equal(0, snapshot.Compensation);
        Assert.Equal(3, settings.Snapshot().Compensation);
    }
    /// <summary>The actual opt-in configuration persists nested exposure values and repairs a null settings object.</summary>
    [Fact]
    public void ConfigurationRoundtripPreservesExposureAndSanitizesNull()
    {
        var config = new VgeConfig { CameraExposure = new() { Enabled = false, Compensation = 1.75f, ManualEV100 = 2, MinEV100 = -8, BrightenRate = 2, DarkenRate = 5 } };
        var restored = JsonConvert.DeserializeObject<VgeConfig>(JsonConvert.SerializeObject(config))!;
        Assert.False(restored.CameraExposure.Enabled);
        Assert.Equal(1.75f, restored.CameraExposure.Compensation);
        Assert.Equal(2, restored.CameraExposure.ManualEV100);
        Assert.Equal(-8, restored.CameraExposure.MinEV100);
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
        Assert.Equal(0, config.CameraExposure.ManualEV100);
        Assert.Equal(-2, config.CameraExposure.MinEV100);
        Assert.Equal(20, config.CameraExposure.MaxEV100);
        Assert.Equal(4, config.CameraExposure.BrightenRate);
        Assert.Equal(8, config.CameraExposure.DarkenRate);
        Assert.Null(JObject.FromObject(config)["PhotometricVersion"]);

        var definition = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/config/configlib-patches.json")));
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
