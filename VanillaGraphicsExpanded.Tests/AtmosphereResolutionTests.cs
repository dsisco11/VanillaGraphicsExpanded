using System.Numerics;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks configurable sky resolution and coherent publication while resolution changes.</summary>
public sealed class AtmosphereResolutionTests
{
    #region Configuration
    /// <summary>Quality survives serialization, clamps safely and scales both dimensions linearly together.</summary>
    [Theory]
    [InlineData("{}", 0, 32, 24)]
    [InlineData("{\"Atmosphere\":null}", 0, 32, 24)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":0}}", 0, 32, 24)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":1}}", 1, 64, 48)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":2}}", 2, 96, 72)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":3}}", 3, 128, 96)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":-2147483648}}", 0, 32, 24)]
    [InlineData("{\"Atmosphere\":{\"SkyLutQuality\":2147483647}}", 3, 128, 96)]
    public void QualitySanitizesAndRoundTrips(string json, int quality, int width, int height)
    {
        var config = JsonConvert.DeserializeObject<VgeConfig>(json)!;
        config.Sanitize();
        string serialized = JsonConvert.SerializeObject(config);
        var restored = JsonConvert.DeserializeObject<VgeConfig>(serialized)!;
        restored.Sanitize();
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

    #region Publication
    /// <summary>Initial completion honors requested dimensions and fills every RGBA sample.</summary>
    [Theory]
    [InlineData(16, 8)]
    [InlineData(33, 25)]
    [InlineData(128, 96)]
    public void InitialSnapshotUsesRequestedDimensions(int width, int height)
    {
        var lookup = new AtmosphereLookup();
        Assert.True(lookup.Update(Vector3.UnitY, 0, 0, complete: true, width: width, height: height));
        AssertSnapshot(lookup.Current!, width, height);
        Assert.False(lookup.Update(Vector3.UnitY, 0, 0, width: width, height: height));
        Assert.Equal(1, lookup.Revision);
    }

    /// <summary>Resizing publishes every sample immediately and preserves the previous immutable table.</summary>
    [Fact]
    public void ResizePublishesCompleteSnapshotImmediately()
    {
        var lookup = new AtmosphereLookup();
        Assert.True(lookup.Update(Vector3.UnitY, 0, 0, complete: true));
        var previous = lookup.Current!;
        float[] previousPixels = previous.Sky.ToArray();
        Assert.True(lookup.Update(Vector3.UnitY, 0, 0, width: 17, height: 9));
        AssertSnapshot(lookup.Current!, 17, 9);
        Assert.Equal(previousPixels, previous.Sky.ToArray());
        Assert.Equal(2, lookup.Revision);
        var resized = lookup.Current;
        Assert.False(lookup.Update(Vector3.UnitY, 0, 0, width: 17, height: 9));
        Assert.Same(resized, lookup.Current);
        Assert.Equal(2, lookup.Revision);
    }

    /// <summary>Resolution edits discard pending work and publish the latest lighting inputs in the same call.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizeSupersedesPendingBuild(bool hasCompletedSnapshot)
    {
        var lookup = new AtmosphereLookup();
        if (hasCompletedSnapshot)
            Assert.True(lookup.Update(Vector3.UnitY, 0, 0, complete: true, width: 32, height: 24));
        Assert.False(lookup.Update(Vector3.UnitY, 0, .5f, complete: false, width: 32, height: 24));
        Assert.True(lookup.Update(Vector3.UnitX, 1, 1, width: 17, height: 9));
        AssertSnapshot(lookup.Current!, 17, 9);
        Assert.Equal(Vector3.UnitX, lookup.Current!.Sun);
        // Compare all outputs with a fresh complete build to exclude mixed old/new rows or integrals.
        var expected = new AtmosphereLookup();
        Assert.True(expected.Update(Vector3.UnitX, 1, 1, complete: true, width: 17, height: 9));
        Assert.Equal(expected.Current!.Sky.ToArray(), lookup.Current.Sky.ToArray());
        Assert.Equal(expected.Current.Environment, lookup.Current.Environment);
        Assert.Equal(expected.Current.Horizon, lookup.Current.Horizon);
        Assert.Equal(expected.Current.Solar, lookup.Current.Solar);
        Assert.Equal(expected.Current.Extinction, lookup.Current.Extinction);
        Assert.Equal(hasCompletedSnapshot ? 2 : 1, lookup.Revision);
    }

    /// <summary>Unchanged dimensions retain bounded weather refresh, including its partial final batch.</summary>
    [Fact]
    public void WeatherRefreshStillPublishesOnlyAfterPartialFinalBatch()
    {
        const int width = 17, height = 9;
        var lookup = new AtmosphereLookup();
        Assert.True(lookup.Update(Vector3.UnitY, 0, 0, complete: true, width: width, height: height));
        var previous = lookup.Current!;
        float[] previousPixels = previous.Sky.ToArray();
        int updates = (width * height + AtmosphereLookup.SamplesPerUpdate - 1) / AtmosphereLookup.SamplesPerUpdate;
        for (int update = 0; update < updates - 1; update++)
        {
            Assert.False(lookup.Update(Vector3.UnitX, 0, 1, complete: false, width: width, height: height));
            Assert.Same(previous, lookup.Current);
            Assert.Equal(1, lookup.Revision);
        }
        Assert.True(lookup.Update(Vector3.UnitX, 0, 1, complete: false, width: width, height: height));
        AssertSnapshot(lookup.Current!, width, height);
        Assert.Equal(Vector3.UnitX, lookup.Current!.Sun);
        Assert.Equal(previousPixels, previous.Sky.ToArray());
        Assert.Equal(2, lookup.Revision);
    }
    /// <summary>Checks dimensions, allocation and initialized samples in a completed table.</summary>
    private static void AssertSnapshot(AtmosphereLighting snapshot, int width, int height)
    {
        Assert.Equal(width, snapshot.Width);
        Assert.Equal(height, snapshot.Height);
        Assert.Equal(width * height * 4, snapshot.Sky.Length);
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            Assert.Equal(1f, snapshot.Sky[pixel * 4 + 3]);
            for (int channel = 0; channel < 3; channel++)
                Assert.True(float.IsFinite(snapshot.Sky[pixel * 4 + channel]) && snapshot.Sky[pixel * 4 + channel] >= 0);
        }
    }
    #endregion
}

