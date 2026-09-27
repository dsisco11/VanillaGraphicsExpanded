using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks background lookup ownership, coherent completion and shutdown without timed sleeps.</summary>
public sealed class AtmosphereComputationTests
{
    #region Completion
    /// <summary>The default lookup call calculates the complete table, including a partial final SIMD batch.</summary>
    [Fact]
    public void LookupDefaultsToCompleteBuild()
    {
        var lookup = new AtmosphereLookup();
        Assert.True(lookup.Update(Vector3.UnitY, 1, .2f, width: 33, height: 25));
        Assert.Equal(33 * 25 * 4, lookup.Current!.Sky.Length);
        for (int pixel = 0; pixel < 33 * 25; pixel++) Assert.Equal(1f, lookup.Current.Sky[pixel * 4 + 3]);
        int layer = lookup.Current.Sky.Length;
        Assert.Equal(layer * AtmosphereAerialPerspective.Depth, lookup.Current.AerialRadiance.Length);
        for (int value = 0; value < layer; value++)
        {
            Assert.Equal(value % 4 == 3 ? 1f : 0f, lookup.Current.AerialRadiance[value]);
            Assert.Equal(value % 4 == 3 ? 1f : 0f, lookup.Current.AerialAttenuation[value]);
            float terminal = lookup.Current.AerialRadiance[(AtmosphereAerialPerspective.Depth - 1) * layer + value];
            Assert.InRange(Math.Abs(terminal - lookup.Current.Sky[value]), 0, 1e-6f);
        }
    }

    /// <summary>Admission never publishes synchronously; the completed immutable result matches a full lookup.</summary>
    [Fact]
    public async Task PublishesOnlyCompletedSnapshotAndReusesStableInputs()
    {
        using var computation = new AtmosphereComputation();
        Assert.Null(computation.Update(Vector3.UnitY, 1, .2f, 33, 25));
        var task = Assert.IsAssignableFrom<Task<AtmosphereLighting>>(computation.Pending);
        var completed = await task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Same(completed, computation.Update(Vector3.UnitY, 1, .2f, 33, 25));
        Assert.Null(computation.Pending);
        Assert.Null(computation.Update(Vector3.UnitY * 3, 1, .2f, 33, 25));
        Assert.Null(computation.Pending);
        var expected = new AtmosphereLookup();
        expected.Update(Vector3.UnitY, 1, .2f, width: 33, height: 25);
        Assert.Equal(expected.Current!.Sky.ToArray(), completed.Sky.ToArray());
        Assert.Equal(expected.Current.AerialRadiance.ToArray(), completed.AerialRadiance.ToArray());
        Assert.Equal(expected.Current.AerialAttenuation.ToArray(), completed.AerialAttenuation.ToArray());
        Assert.Equal(expected.Current.Altitude, completed.Altitude);
        Assert.Equal(expected.Current.Environment, completed.Environment);
        Assert.Equal(expected.Current.Horizon, completed.Horizon);
        Assert.Equal(expected.Current.Solar, completed.Solar);
    }

    /// <summary>Completion of admitted work is retained while the newest request starts its own full build.</summary>
    [Fact]
    public async Task CompletionAdmitsLatestInputsAndResolution()
    {
        using var computation = new AtmosphereComputation();
        Assert.Null(computation.Update(Vector3.UnitY, 0, 0, 32, 24));
        var first = await computation.Pending!.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Same(first, computation.Update(Vector3.UnitX, 2, 1, 17, 9));
        var second = await computation.Pending!.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Same(second, computation.Update(Vector3.UnitX, 2, 1, 17, 9));
        Assert.Equal(Vector3.UnitY, first.Sun);
        Assert.Equal(Vector3.UnitX, second.Sun);
        Assert.Equal(17 * 9 * 4, second.Sky.Length);
        Assert.Null(computation.Pending);
    }

    /// <summary>Requested dimensions outside supported bounds share one normalized admission key.</summary>
    [Fact]
    public async Task EquivalentClampedDimensionsDoNotRepeatWork()
    {
        using var computation = new AtmosphereComputation();
        computation.Update(Vector3.UnitY, 0, 0, 0, 0);
        var completed = await computation.Pending!.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Same(completed, computation.Update(Vector3.UnitY, 0, 0, 16, 8));
        Assert.Null(computation.Pending);
    }
    #endregion

    #region Lifetime
    /// <summary>Cancelled builds preserve the previous complete result instead of publishing partial rows.</summary>
    [Fact]
    public void CancelledLookupPreservesPreviousSnapshot()
    {
        var lookup = new AtmosphereLookup();
        lookup.Update(Vector3.UnitY, 0, 0);
        var original = lookup.Current;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => lookup.Update(Vector3.UnitX, 1, 1, cancellationToken: cancellation.Token));
        Assert.Same(original, lookup.Current);
        Assert.Equal(1, lookup.Revision);
    }

    /// <summary>Shutdown prevents publication and safely observes an admitted worker's completion or cancellation.</summary>
    [Fact]
    public async Task DisposalPreventsFurtherAdmissionAndPublication()
    {
        var computation = new AtmosphereComputation();
        computation.Update(Vector3.UnitY, 0, 0, 128, 96);
        var task = computation.Pending!;
        computation.Dispose();
        computation.Dispose();
        Assert.Null(computation.Pending);
        Assert.Throws<ObjectDisposedException>(() => computation.Update(Vector3.UnitY, 0, 0, 32, 24));
        try { await task.WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (OperationCanceledException) { Assert.True(task.IsCanceled); }
    }
    #endregion
}
