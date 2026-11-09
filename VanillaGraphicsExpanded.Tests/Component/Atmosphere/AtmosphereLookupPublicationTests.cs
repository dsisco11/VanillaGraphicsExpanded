using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Protects bounded atmosphere refresh and coherent publication under changing inputs.</summary>
public sealed class AtmosphereLookupPublicationTests
{
    private const int RefreshWidth = 17;
    private const int RefreshHeight = 9;
    private const int RefreshUpdates = (RefreshWidth * RefreshHeight + AtmosphereLookup.SamplesPerUpdate - 1) / AtmosphereLookup.SamplesPerUpdate;
    private const int ChangingWidth = 20;
    private const int ChangingHeight = 20;
    private const int ChangingUpdates = (ChangingWidth * ChangingHeight + AtmosphereLookup.SamplesPerUpdate - 1) / AtmosphereLookup.SamplesPerUpdate;
    private const int DefaultUpdates = (AtmosphereLookup.DefaultWidth * AtmosphereLookup.DefaultHeight + AtmosphereLookup.SamplesPerUpdate - 1) / AtmosphereLookup.SamplesPerUpdate;
    #region Public API
    /// <summary>Synchronous initialization publishes a complete table; subsequent bounded refresh retains it until completion.</summary>
    [Fact]
    public void RefreshNeverPublishesPartialResults()
    {
        // Arrange an empty lookup and a fixed refresh resolution for comparing complete and sliced publication.
        var lookup = new AtmosphereLookup();
        // Act through initial completion and bounded refresh slices; assert the old immutable table remains until the final slice.
        Assert.True(lookup.Update(Vector3.UnitY, 0, 0, complete: true, width: RefreshWidth, height: RefreshHeight));
        var original = Assert.IsType<AtmosphereLighting>(lookup.Current);
        Assert.Equal(RefreshWidth, original.Width);
        Assert.Equal(RefreshHeight, original.Height);
        Assert.Equal(RefreshWidth * RefreshHeight * 4, original.Sky.Length);
        for (int pixel = 0; pixel < RefreshWidth * RefreshHeight; pixel++)
        {
            Assert.Equal(1f, original.Sky[pixel * 4 + 3]);
            for (int channel = 0; channel < 3; channel++)
                Assert.True(float.IsFinite(original.Sky[pixel * 4 + channel]) && original.Sky[pixel * 4 + channel] >= 0);
        }
        Assert.False(lookup.Update(Vector3.UnitY, 0, 0, complete: true, width: RefreshWidth, height: RefreshHeight));
        Assert.Same(original, lookup.Current);
        Assert.Equal(1, lookup.Revision);
        float[] originalPixels = original.Sky.ToArray();
        Assert.Equal(RefreshWidth * RefreshHeight * 4, originalPixels.Length);
        // Incomplete refresh slices must preserve both the published reference and its immutable pixel content.
        for (int update = 0; update < RefreshUpdates - 1; update++)
        {
            Assert.False(lookup.Update(Vector3.UnitY, 0, 1, complete: false, width: RefreshWidth, height: RefreshHeight));
            Assert.Same(original, lookup.Current);
            Assert.Equal(1, lookup.Revision);
        }
        // The final bounded slice swaps in a complete new snapshot and advances the revision once.
        Assert.True(lookup.Update(Vector3.UnitY, 0, 1, complete: false, width: RefreshWidth, height: RefreshHeight));
        Assert.NotSame(original, lookup.Current);
        var refreshed = lookup.Current!;
        Assert.Equal(RefreshWidth, refreshed.Width);
        Assert.Equal(RefreshHeight, refreshed.Height);
        Assert.Equal(RefreshWidth * RefreshHeight * 4, refreshed.Sky.Length);
        for (int pixel = 0; pixel < RefreshWidth * RefreshHeight; pixel++)
        {
            Assert.Equal(1f, refreshed.Sky[pixel * 4 + 3]);
            for (int channel = 0; channel < 3; channel++)
                Assert.True(float.IsFinite(refreshed.Sky[pixel * 4 + channel]) && refreshed.Sky[pixel * 4 + channel] >= 0);
        }
        Assert.Equal(originalPixels, original.Sky.ToArray());
        Assert.Equal(2, lookup.Revision);
        Assert.True(lookup.Current!.Solar.X < original.Solar.X);
    }

    /// <summary>Changing weather cannot restart an admitted build and starve its publication.</summary>
    [Fact]
    public void ChangingInputsFinishAdmittedSnapshotThenRefreshLatest()
    {
        // Arrange a lookup and the exact bounded-update count for its chosen resolution.
        int updates = ChangingUpdates;
        var lookup = new AtmosphereLookup();
        // Act by changing inputs while a table is admitted; assert that current work completes before latest inputs refresh.
        Assert.False(lookup.Update(Vector3.UnitY, 0, 0, complete: false, width: ChangingWidth, height: ChangingHeight));
        // Keep the first build admitted while changing requests; the changes must not starve its completion.
        for (int update = 1; update < updates - 1; update++)
            Assert.False(lookup.Update(Vector3.Normalize(new Vector3(update, 10, 1)), update, update / (float)updates, complete: false, width: ChangingWidth, height: ChangingHeight));
        // Completing the admitted table publishes its original inputs before a second build adopts the latest ones.
        Assert.True(lookup.Update(Vector3.UnitX, 2, 1, complete: false, width: ChangingWidth, height: ChangingHeight));
        Assert.Equal(Vector3.UnitY, lookup.Current!.Sun);
        for (int update = 0; update < updates - 1; update++) Assert.False(lookup.Update(Vector3.UnitX, 2, 1, complete: false, width: ChangingWidth, height: ChangingHeight));
        Assert.True(lookup.Update(Vector3.UnitX, 2, 1, complete: false, width: ChangingWidth, height: ChangingHeight));
        Assert.Equal(Vector3.UnitX, lookup.Current!.Sun);
        Assert.Equal(2, lookup.Revision);
    }

    /// <summary>Equivalent normalized directions and sub-key jitter reuse the table; invalid inputs cannot corrupt it.</summary>
    [Fact]
    public void StableQuantizedInputsDoNotRefresh()
    {
        // Arrange a lookup, then complete one table to establish a cached quantized input key.
        var lookup = new AtmosphereLookup();
        // Fill one table, then probe normalized directions and weather values that share its quantized cache key.
        for (int update = 0; update < RefreshUpdates; update++)
            lookup.Update(Vector3.UnitY, 0, 0, complete: false, width: RefreshWidth, height: RefreshHeight);
        var original = lookup.Current;
        // Act and Assert: equivalent inputs and invalid values must leave that completed snapshot unchanged.
        for (int update = 0; update < RefreshUpdates + 1; update++)
            Assert.False(lookup.Update(Vector3.UnitY * 7, .001f, .001f, width: RefreshWidth, height: RefreshHeight));
        Assert.False(lookup.Update(Vector3.Zero, 0, 0));
        Assert.False(lookup.Update(new Vector3(float.NaN), 0, 0));
        Assert.False(lookup.Update(Vector3.UnitY, float.PositiveInfinity, 0));
        Assert.Same(original, lookup.Current);
        Assert.Equal(1, lookup.Revision);
    }
    #endregion
}
