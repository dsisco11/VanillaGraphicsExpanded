using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Verifies persisted collision-traversal defaults and value sanitization.</summary>
public sealed class SurfaceTraversalBudgetTests
{
    #region Public API
    /// <summary>Missing configuration fields receive sufficient traversal steps and finite distance.</summary>
    [Fact]
    public void DefaultsCoverFiniteDistance()
    {
        // Arrange and act: deserialize the minimal nested settings object.
        // Act
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<VgeConfig>("{\"LumOn\":{\"LumonScene\":{}}}")!;

        // Assert: defaults cover the supported finite trace segment.
        Assert.Equal(512, config.LumOn.LumonScene.RelightMaxTraceDistance);
        Assert.Equal(1024, config.LumOn.LumonScene.RelightMaxDdaSteps);
    }

    /// <summary>Persisted values retain supported custom limits and clamp invalid ranges.</summary>
    [Theory]
    [InlineData(-1, -1, 1, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(96, 64, 96, 64)]
    [InlineData(999, 9999, 512, 1024)]
    public void LimitsRoundTripAndSanitize(int distance, int steps, int expectedDistance, int expectedSteps)
    {
        // Arrange: author both supported and out-of-range persisted values.
        var config = new VgeConfig();
        config.LumOn.LumonScene.RelightMaxTraceDistance = distance;
        config.LumOn.LumonScene.RelightMaxDdaSteps = steps;

        // Act: serialize, deserialize, and apply the production sanitization rule.
        var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<VgeConfig>(Newtonsoft.Json.JsonConvert.SerializeObject(config))!;
        copy.Sanitize();

        // Assert: values are preserved within bounds and clamped outside them.
        Assert.Equal(expectedDistance, copy.LumOn.LumonScene.RelightMaxTraceDistance);
        Assert.Equal(expectedSteps, copy.LumOn.LumonScene.RelightMaxDdaSteps);
    }
    #endregion
}










