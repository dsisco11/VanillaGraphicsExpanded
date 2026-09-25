using Newtonsoft.Json;
using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

/// <summary>Checks defaults, persistence and bounds for independent update allocations.</summary>
public sealed class SurfaceLightingBudgetConfigTests
{
    #region Persistence
    /// <summary>New configurations receive measured independent defaults.</summary>
    [Fact]
    public void NewConfigUsesIndependentDefaults()
    {
        var config = JsonConvert.DeserializeObject<VgeConfig>("{}")!.LumOn.LumonScene;
        Assert.Equal(8, config.RelightSeedPagesPerFrame);
        Assert.Equal(8, config.RelightDirectPagesPerFrame);
        Assert.Equal(4, config.RelightIndirectPagesPerFrame);
    }

    /// <summary>Independent stage settings survive serialization, including a disabled indirect allocation.</summary>
    [Fact]
    public void IndependentValuesRoundTrip()
    {
        var config = new VgeConfig();
        var scene = config.LumOn.LumonScene;
        scene.RelightSeedPagesPerFrame = 7; scene.RelightDirectPagesPerFrame = 13; scene.RelightIndirectPagesPerFrame = 0;
        string json = JsonConvert.SerializeObject(config);
        var restored = JsonConvert.DeserializeObject<VgeConfig>(json)!.LumOn.LumonScene;
        Assert.Equal(7, restored.RelightSeedPagesPerFrame); Assert.Equal(13, restored.RelightDirectPagesPerFrame);
        Assert.Equal(0, restored.RelightIndirectPagesPerFrame);
    }

    /// <summary>Each allocation is bounded independently while zero remains a valid disabled stage.</summary>
    [Theory]
    [InlineData(-1, 0)] [InlineData(0, 0)] [InlineData(16, 16)] [InlineData(257, 256)]
    public void SanitizationClampsEveryStage(int value, int expected)
    {
        var config = new VgeConfig(); var scene = config.LumOn.LumonScene;
        scene.RelightSeedPagesPerFrame = scene.RelightDirectPagesPerFrame = scene.RelightIndirectPagesPerFrame = value;
        config.Sanitize();
        Assert.Equal(expected, scene.RelightSeedPagesPerFrame); Assert.Equal(expected, scene.RelightDirectPagesPerFrame);
        Assert.Equal(expected, scene.RelightIndirectPagesPerFrame);
    }
    #endregion
}
