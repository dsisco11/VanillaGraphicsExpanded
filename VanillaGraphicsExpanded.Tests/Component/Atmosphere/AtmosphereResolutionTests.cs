using System.Numerics;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Checks configurable sky resolution and coherent publication while resolution changes.</summary>
public sealed class AtmosphereResolutionTests
{
    #region Public API
    /// <summary>Initial completion honors requested dimensions and fills every RGBA sample.</summary>
    [Theory]
    [InlineData(16, 8)]
    [InlineData(128, 96)]
    public void InitialSnapshotUsesRequestedDimensions(int width, int height)
    {
        // Arrange an empty lookup and use the requested dimensions supplied by each theory row.
        var lookup = new AtmosphereLookup();
        // Act by building at the requested size; assert complete allocation and reuse without a revision change.
        Assert.True(lookup.Update(Vector3.UnitY, 0, 0, complete: true, width: width, height: height));
        AssertSnapshot(lookup.Current!, width, height);
        Assert.False(lookup.Update(Vector3.UnitY, 0, 0, width: width, height: height));
        Assert.Equal(1, lookup.Revision);
    }

    /// <summary>Resizing publishes every sample immediately and preserves the previous immutable table.</summary>
    [Fact]
    public void ResizePublishesCompleteSnapshotImmediately()
    {
        // Arrange an empty lookup so both the original and resized snapshots can be retained.
        var lookup = new AtmosphereLookup();
        // Act by completing the initial size and resizing; assert the new snapshot is complete while old pixels remain immutable.
        Assert.True(lookup.Update(Vector3.UnitY, 0, 0, complete: true, width: 17, height: 9));
        var previous = lookup.Current!;
        float[] previousPixels = previous.Sky.ToArray();
        Assert.True(lookup.Update(Vector3.UnitY, 0, 0, width: 16, height: 9));
        AssertSnapshot(lookup.Current!, 16, 9);
        Assert.Equal(previousPixels, previous.Sky.ToArray());
        Assert.Equal(2, lookup.Revision);
        var resized = lookup.Current;
        Assert.False(lookup.Update(Vector3.UnitY, 0, 0, width: 16, height: 9));
        Assert.Same(resized, lookup.Current);
        Assert.Equal(2, lookup.Revision);
    }

    /// <summary>Resolution edits discard pending work and publish the latest lighting inputs in the same call.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizeSupersedesPendingBuild(bool hasCompletedSnapshot)
    {
        // Arrange an empty lookup, optionally with a completed baseline according to the theory row.
        var lookup = new AtmosphereLookup();
        if (hasCompletedSnapshot)
            Assert.True(lookup.Update(Vector3.UnitY, 0, 0, complete: true, width: 17, height: 9));
        // Act and Assert: start pending work, resize it, and verify the latest dimensions publish atomically.
        Assert.False(lookup.Update(Vector3.UnitY, 0, .5f, complete: false, width: 17, height: 9));
        Assert.True(lookup.Update(Vector3.UnitX, 1, 1, width: 16, height: 9));
        AssertSnapshot(lookup.Current!, 16, 9);
        Assert.Equal(Vector3.UnitX, lookup.Current!.Sun);
        // Compare all outputs with a fresh complete build to exclude mixed old/new rows or integrals.
        var expected = new AtmosphereLookup();
        Assert.True(expected.Update(Vector3.UnitX, 1, 1, complete: true, width: 16, height: 9));
        Assert.Equal(expected.Current!.Sky.ToArray(), lookup.Current.Sky.ToArray());
        Assert.Equal(expected.Current.Environment, lookup.Current.Environment);
        Assert.Equal(expected.Current.Horizon, lookup.Current.Horizon);
        Assert.Equal(expected.Current.Solar, lookup.Current.Solar);
        Assert.Equal(expected.Current.Extinction, lookup.Current.Extinction);
        Assert.Equal(hasCompletedSnapshot ? 2 : 1, lookup.Revision);
    }
    #endregion

    #region Private
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
