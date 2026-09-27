using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Protects bounded atmosphere refresh and coherent publication under changing inputs.</summary>
public sealed class AtmosphereLookupTests
{
    private const int Updates = AtmosphereLookup.DefaultWidth * AtmosphereLookup.DefaultHeight / AtmosphereLookup.SamplesPerUpdate;

    #region Publication lifetime
    /// <summary>Synchronous initialization publishes a complete table; subsequent bounded refresh retains it until completion.</summary>
    [Fact]
    public void RefreshNeverPublishesPartialResults()
    {
        var lookup = new AtmosphereLookup();
        Assert.True(lookup.Update(Vector3.UnitY, 0, 0, complete: true));
        var original = Assert.IsType<AtmosphereLighting>(lookup.Current);
        Assert.False(lookup.Update(Vector3.UnitY, 0, 0, complete: true));
        Assert.Same(original, lookup.Current);
        Assert.Equal(1, lookup.Revision);
        float[] originalPixels = original.Sky.ToArray();
        Assert.Equal(AtmosphereLookup.DefaultWidth * AtmosphereLookup.DefaultHeight * 4, originalPixels.Length);
        for (int update = 0; update < Updates - 1; update++)
        {
            Assert.False(lookup.Update(Vector3.UnitY, 0, 1, complete: false));
            Assert.Same(original, lookup.Current);
            Assert.Equal(1, lookup.Revision);
        }
        Assert.True(lookup.Update(Vector3.UnitY, 0, 1, complete: false));
        Assert.NotSame(original, lookup.Current);
        Assert.Equal(originalPixels, original.Sky.ToArray());
        Assert.Equal(2, lookup.Revision);
        Assert.True(lookup.Current!.Solar.X < original.Solar.X);
    }

    /// <summary>Changing weather cannot restart an admitted build and starve its publication.</summary>
    [Fact]
    public void ChangingInputsFinishAdmittedSnapshotThenRefreshLatest()
    {
        var lookup = new AtmosphereLookup();
        Assert.False(lookup.Update(Vector3.UnitY, 0, 0, complete: false));
        for (int update = 1; update < Updates - 1; update++)
            Assert.False(lookup.Update(Vector3.Normalize(new Vector3(update, 10, 1)), update, update / (float)Updates, complete: false));
        Assert.True(lookup.Update(Vector3.UnitX, 2, 1, complete: false));
        Assert.Equal(Vector3.UnitY, lookup.Current!.Sun);
        for (int update = 0; update < Updates - 1; update++) Assert.False(lookup.Update(Vector3.UnitX, 2, 1, complete: false));
        Assert.True(lookup.Update(Vector3.UnitX, 2, 1, complete: false));
        Assert.Equal(Vector3.UnitX, lookup.Current!.Sun);
        Assert.Equal(2, lookup.Revision);
    }

    /// <summary>Equivalent normalized directions and sub-key jitter reuse the table; invalid inputs cannot corrupt it.</summary>
    [Fact]
    public void StableQuantizedInputsDoNotRefresh()
    {
        var lookup = new AtmosphereLookup();
        for (int update = 0; update < Updates; update++) lookup.Update(Vector3.UnitY, 0, 0, complete: false);
        var original = lookup.Current;
        for (int update = 0; update < Updates + 1; update++) Assert.False(lookup.Update(Vector3.UnitY * 7, .001f, .001f));
        Assert.False(lookup.Update(Vector3.Zero, 0, 0));
        Assert.False(lookup.Update(new Vector3(float.NaN), 0, 0));
        Assert.False(lookup.Update(Vector3.UnitY, float.PositiveInfinity, 0));
        Assert.Same(original, lookup.Current);
        Assert.Equal(1, lookup.Revision);
    }
    /// <summary>Records warm CPU cost for the bounded update and complete refresh without imposing machine-specific thresholds.</summary>
    [Fact]
    public void RecordBoundedUpdateCost()
    {
        for (int warm = 0; warm < 2; warm++)
        {
            var warmLookup = new AtmosphereLookup();
            for (int update = 0; update < Updates; update++) warmLookup.Update(Vector3.UnitY, 0, .5f, complete: false);
        }
        var elapsed = new double[5];
        for (int run = 0; run < elapsed.Length; run++)
        {
            var lookup = new AtmosphereLookup();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int update = 0; update < Updates; update++) lookup.Update(Vector3.UnitY, 0, .5f, complete: false);
            timer.Stop();
            elapsed[run] = timer.Elapsed.TotalMilliseconds;
            Assert.Equal(1, lookup.Revision);
        }
        Array.Sort(elapsed);
        Xunit.TestContext.Current.TestOutputHelper!.WriteLine($"Warm median complete refresh: {elapsed[2]:F3} ms; amortized bounded update: {elapsed[2] / Updates:F3} ms; five runs range: {elapsed[0]:F3}..{elapsed[^1]:F3} ms.");
    }
    #endregion
}
