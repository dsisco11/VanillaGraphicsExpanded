using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Verifies persisted selection of world-probe GPU routing.</summary>
public sealed class WorldProbeRoutingConfigurationTests
{
    #region Public API
    /// <summary>The existing route default remains enabled and explicit choices survive serialization.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RoutingFlagDefaultsOnAndRoundTrips(bool enabled)
    {
        // Arrange: load defaults and author an explicit route selection.
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<VgeConfig>("{}")!;
        Assert.True(config.WorldProbeClipmap.EnableGpuTracing);
        config.WorldProbeClipmap.EnableGpuTracing = enabled;

        // Act: persist and sanitize the explicit setting.
        config = Newtonsoft.Json.JsonConvert.DeserializeObject<VgeConfig>(Newtonsoft.Json.JsonConvert.SerializeObject(config))!;
        config.Sanitize();

        // Assert: the selected route survives round-trip serialization.
        Assert.Equal(enabled, config.WorldProbeClipmap.EnableGpuTracing);
    }
    #endregion
}










