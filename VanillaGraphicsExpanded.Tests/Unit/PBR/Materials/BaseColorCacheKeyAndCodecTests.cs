using System;
using System.Globalization;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks base-color cache key identity and culture-independent serialization inputs.</summary>
[Trait("Category", "Unit")]
public sealed class BaseColorCacheKeyAndCodecTests
{
    #region Public API
    /// <summary>Base-color keys are stable across calls and independent of the current culture.</summary>
    [Fact]
    public void BaseColorKeys_AreStable_AcrossCalls_AndCultureIndependent()
    {
        // Arrange: build a key from fixed texture identity and cache inputs.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            var inputs = BaseColorCacheKeyInputs.CreateDefaults();
            var builder = new BaseColorCacheKeyBuilder();
            var texture = new AssetLocation("game", "textures/block/test.png");

            AtlasCacheKey first = builder.BuildKey(inputs, texture, originPath: "assets/game/textures/block/test.png", assetBytes: 12345);

            // Act: rebuild the same identity under a culture with different number formatting conventions.
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            AtlasCacheKey second = builder.BuildKey(inputs, texture, originPath: "assets/game/textures/block/test.png", assetBytes: 12345);

            // Assert: formatting culture does not alter cache identity.
            Assert.Equal(first, second);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>Origin path, asset bytes, and averaging algorithm version each contribute to key identity.</summary>
    [Fact]
    public void BaseColorKeys_Change_WhenOriginBytesOrAvgAlgoChanges()
    {
        // Arrange: hold texture and other inputs fixed while varying one identity component at a time.
        var inputs = BaseColorCacheKeyInputs.CreateDefaults();
        var builder = new BaseColorCacheKeyBuilder();
        var texture = new AssetLocation("game", "textures/block/test.png");
        AtlasCacheKey baseline = builder.BuildKey(inputs, texture, originPath: "a", assetBytes: 10);

        // Act: change source identity and algorithm version independently.
        AtlasCacheKey changedPath = builder.BuildKey(inputs, texture, originPath: "b", assetBytes: 10);
        AtlasCacheKey changedBytes = builder.BuildKey(inputs, texture, originPath: "a", assetBytes: 11);
        var olderAlgorithm = new BaseColorCacheKeyInputs(
            SchemaVersion: inputs.SchemaVersion,
            StablePrefix: inputs.StablePrefix.Replace("avgAlgo=2", "avgAlgo=1", StringComparison.Ordinal));
        AtlasCacheKey changedAlgorithm = builder.BuildKey(olderAlgorithm, texture, originPath: "a", assetBytes: 10);

        // Assert: changing any cache-key input changes identity.
        Assert.NotEqual(baseline, changedPath);
        Assert.NotEqual(baseline, changedBytes);
        Assert.NotEqual(baseline, changedAlgorithm);
    }
    #endregion
}
