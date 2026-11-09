using Newtonsoft.Json;
using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Checks configured atmosphere quality controls its derived sky lookup dimensions.</summary>
public sealed class AtmosphereResolutionTests
{
    #region Public API
    /// <summary>Quality survives serialization, clamps safely and scales both dimensions linearly together.</summary>
    [Theory]
    [InlineData("{}", 2, 96, 72)]
    [InlineData("{\"Atmosphere\":null}", 2, 96, 72)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":0}}", 0, 32, 24)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":1}}", 1, 64, 48)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":2}}", 2, 96, 72)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":3}}", 3, 128, 96)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":-2147483648}}", 0, 32, 24)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":2147483647}}", 3, 128, 96)]
    public void QualitySanitizesAndRoundTrips(string json, int quality, int width, int height)
    {
        // Arrange each legacy/explicit configuration document and its expected sanitized dimensions.
        var config = JsonConvert.DeserializeObject<VgeConfig>(json)!;
        // Act by sanitizing, serializing and restoring the configuration before checking persisted quality.
        config.Sanitize();
        string serialized = JsonConvert.SerializeObject(config);
        var restored = JsonConvert.DeserializeObject<VgeConfig>(serialized)!;
        restored.Sanitize();
        // Assert the restored quality and derived dimensions, with no serialized derived-size fields.
        Assert.Equal(quality, restored.Atmosphere.SkyLutQuality);
        Assert.Equal(width, restored.Atmosphere.LookupWidth);
        Assert.Equal(height, restored.Atmosphere.LookupHeight);
        Assert.Contains("\"SkyLutQuality\":", serialized);
        Assert.DoesNotContain("SkyLutWidth", serialized);
        Assert.DoesNotContain("SkyLutHeight", serialized);
        Assert.DoesNotContain("LookupWidth", serialized);
        Assert.DoesNotContain("LookupHeight", serialized);
    }
    #endregion
}
