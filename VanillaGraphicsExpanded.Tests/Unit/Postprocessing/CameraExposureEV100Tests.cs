using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR.CameraExposure;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Qualifies calibrated camera authoring and one-time legacy field interpretation independently of shader helpers.</summary>
public sealed class CameraExposureEV100Tests
{
    #region Public API
    /// <summary>A saturation-calibrated camera doubles or halves gain per stop and round trips throughout its domain.</summary>
    [Theory]
    [InlineData(-24f)] [InlineData(-2f)] [InlineData(0f)] [InlineData(13.5f)] [InlineData(48f)]
    public void CameraConversionMatchesIndependentSaturationEquation(float camera)
    {
        // Arrange
        const double attenuation = .78, saturationCoefficient = .78;
        double lmax = saturationCoefficient / attenuation;
        double gain = 1 / (lmax * Math.Pow(2, camera));

        // Act
        double actual = Math.Pow(2, ExposureCalibration.ToLogGain(camera));

        // Assert
        Assert.InRange(Math.Abs(actual / gain - 1), 0, .000001);
        Assert.Equal(camera, ExposureCalibration.FromLogGain(ExposureCalibration.ToLogGain(camera)));
        Assert.InRange(Math.Abs(Math.Pow(2, ExposureCalibration.ToLogGain(camera + 1)) / actual - .5), 0, .000001);
    }

    /// <summary>Scene values represent the camera setting that places measured physical nits at authored middle gray.</summary>
    [Theory]
    [InlineData(.0001, .18)] [InlineData(.18, .18)] [InlineData(1, .09)]
    [InlineData(5730, .18)] [InlineData(70000000, .36)]
    public void PhysicalSceneEV100MapsToMiddleGray(double nits, double gray)
    {
        // Arrange
        double expected = Math.Log2(nits / gray);

        // Act
        double actual = ExposureCalibration.SceneEV100((float)Math.Log2(nits), (float)gray);

        // Assert
        Assert.InRange(Math.Abs(actual - expected), 0, .000004);
        Assert.InRange(Math.Abs(nits * Math.Pow(2, ExposureCalibration.ToLogGain((float)actual)) / gray - 1), 0, .000004);
    }

    /// <summary>Field presence chooses current or legacy identities before finite validation and bound reversal.</summary>
    [Theory]
    [InlineData("{}", 0, -20, 2)]
    [InlineData("{\"ManualEV\":-3,\"MinEV\":-9,\"MaxEV\":4}", -3, -9, 4)]
    [InlineData("{\"MinEV\":7,\"MaxEV\":-7}", 0, 7, 7)]
    [InlineData("{\"MinEV\":-9}", 0, -9, 2)]
    [InlineData("{\"MaxEV\":8}", 0, -20, 8)]
    [InlineData("{\"ManualEV100\":3,\"MinEV100\":-4,\"MaxEV100\":9}", -3, -9, 4)]
    [InlineData("{\"ManualEV\":8,\"ManualEV100\":3,\"MinEV\":-9,\"MaxEV\":4,\"MinEV100\":-1}", -3, -9, 1)]
    [InlineData("{\"ManualEV100\":null,\"ManualEV\":8,\"MinEV100\":\"bad\",\"MaxEV100\":null,\"MinEV\":-9,\"MaxEV\":8}", 0, -20, 2)]
    [InlineData("{\"ManualEV\":\"bad\",\"MinEV\":null,\"MaxEV\":\"bad\"}", 0, -20, 2)]
    [InlineData("{\"ManualEV100\":99,\"MinEV100\":9,\"MaxEV100\":-9}", -48, -9, -9)]
    [InlineData("{\"ManualEV\":NaN,\"MinEV\":Infinity,\"MaxEV\":-Infinity}", 0, -20, 2)]
    public void PersistedFieldsProduceEquivalentGain(string json, float manual, float minimum, float maximum)
    {
        // Arrange the persisted JSON fields whose identities and presence determine legacy interpretation.

        // Act
        var settings = JsonConvert.DeserializeObject<CameraExposureSettings>(json)!;
        var snapshot = settings.Snapshot();

        // Assert the migrated snapshot and the canonical persisted representation.
        Assert.Equal(manual, snapshot.ManualLogGain);
        Assert.Equal(minimum, snapshot.MinLogGain);
        Assert.Equal(maximum, snapshot.MaxLogGain);
        // Act and assert that the canonical fields round-trip to the same snapshot.
        var saved = JObject.FromObject(settings);
        Assert.Null(saved["ManualEV"]); Assert.Null(saved["MinEV"]); Assert.Null(saved["MaxEV"]);
        Assert.NotNull(saved["ManualEV100"]); Assert.NotNull(saved["MinEV100"]); Assert.NotNull(saved["MaxEV100"]);
        Assert.Equal(snapshot, saved.ToObject<CameraExposureSettings>()!.Snapshot());
    }

    /// <summary>Document migration occurs once and preserves current invalid identity precedence before population.</summary>
    [Fact]
    public void DocumentMigrationIsIdempotentAndLeavesOtherControlsAlone()
    {
        // Arrange
        var document = JObject.Parse("{\"ManualEV\":-2,\"MinEV\":-11,\"MaxEV\":6,\"ManualEV100\":null,\"Enabled\":false,\"Compensation\":1.5}");

        // Act
        bool migrated = CameraExposureSettingsConverter.Migrate(document);

        // Assert
        Assert.True(migrated);
        Assert.Equal(0, document["ManualEV100"]!.Value<float>());
        var once = document.DeepClone();
        // Act / Assert: the second migration reports no changes and preserves the document.
        Assert.False(CameraExposureSettingsConverter.Migrate(document));
        Assert.True(JToken.DeepEquals(once, document));
        Assert.False(document["Enabled"]!.Value<bool>());
        Assert.Equal(1.5f, document["Compensation"]!.Value<float>());
    }

    /// <summary>Camera and local opt-outs, custom gray, compensation and rates retain effective behavior on repeated loads.</summary>
    [Fact]
    public void LegacyConfigurationPreservesExplicitChoicesAndGain()
    {
        // Arrange / Act: deserialize the inline legacy JSON, sanitize it, and capture the effective policy.
        var config = JsonConvert.DeserializeObject<VgeConfig>("""
            {"CameraExposure":{"Enabled":false,"ManualEV":-3,"MinEV":-9,"MaxEV":4,"Compensation":1.5,
              "MiddleGray":0.09,"BrightenRate":2,"DarkenRate":5},
             "Postprocessing":{"LocalExposure":{"Enabled":false}}}
            """)!;
        config.Sanitize();
        var policy = config.CameraExposure.Snapshot();

        // Assert the restored choices and effective gain.
        Assert.False(policy.Enabled); Assert.False(config.Postprocessing.LocalExposure.Enabled);
        Assert.Equal(-1.5f, Math.Clamp(policy.ManualLogGain + policy.Compensation, policy.MinLogGain, policy.MaxLogGain));
        Assert.Equal(.09f, policy.MiddleGray); Assert.Equal(2, policy.BrightenRate); Assert.Equal(5, policy.DarkenRate);
        // Act / Assert: serialize the sanitized configuration and verify its canonical field names.
        string once = JsonConvert.SerializeObject(config);
        Assert.DoesNotContain("\"ManualEV\":", once); Assert.DoesNotContain("PhotometricVersion", once);
        // Act: deserialize the canonical representation a second time.
        var restored = JsonConvert.DeserializeObject<VgeConfig>(once)!;

        // Assert the second load preserves the same policy and serialization.
        Assert.Equal(policy, restored.CameraExposure.Snapshot());
        Assert.Equal(once, JsonConvert.SerializeObject(restored));
    }
    #endregion
}

