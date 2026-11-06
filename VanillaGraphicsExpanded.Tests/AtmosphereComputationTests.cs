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
        Assert.Equal(33, lookup.Current!.Width);
        Assert.Equal(25, lookup.Current.Height);
        Assert.Equal(33 * 25 * 4, lookup.Current!.Sky.Length);
        for (int pixel = 0; pixel < 33 * 25; pixel++)
        {
            Assert.Equal(1f, lookup.Current.Sky[pixel * 4 + 3]);
            for (int channel = 0; channel < 3; channel++)
                Assert.True(float.IsFinite(lookup.Current.Sky[pixel * 4 + channel]) && lookup.Current.Sky[pixel * 4 + channel] >= 0);
        }
        int layer = lookup.Current.Sky.Length;
        Assert.Equal(layer * AtmosphereAerialPerspective.Depth, lookup.Current.AerialRadiance.Length);
        for (int value = 0; value < layer; value++)
        {
            Assert.Equal(value % 4 == 3 ? 1f : 0f, lookup.Current.AerialRadiance[value]);
            Assert.Equal(value % 4 == 3 ? 1f : 0f, lookup.Current.AerialAttenuation[value]);
            float terminal = lookup.Current.AerialRadiance[(AtmosphereAerialPerspective.Depth - 1) * layer + value];
            Assert.InRange(Math.Abs(terminal - lookup.Current.Sky[value]), 0, 1e-6f + 2e-6f * Math.Abs(lookup.Current.Sky[value]));
        }
        var complete = lookup.Current;
        Assert.False(lookup.Update(Vector3.UnitY, 1, .2f, width: 33, height: 25));
        Assert.Same(complete, lookup.Current);
        Assert.Equal(1, lookup.Revision);
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
    public void CompletionAdmitsLatestInputsAndResolution()
    {
        var producer = new ControlledProducer();
        using var computation = new AtmosphereComputation(producer.Start);
        Assert.Null(computation.Update(Vector3.UnitY, 0, 0, 32, 24));
        Assert.Null(computation.Update(Vector3.UnitX, 2, 1, 17, 9));
        Assert.Single(producer.Requests);
        var first = producer.Complete(0);
        Assert.Same(first, computation.Update(Vector3.UnitX, 2, 1, 17, 9));
        var second = producer.Complete(1);
        Assert.Same(second, computation.Update(Vector3.UnitX, 2, 1, 17, 9));
        Assert.Equal(Vector3.UnitY, first.Sun);
        Assert.Equal(Vector3.UnitX, second.Sun);
        Assert.Equal((17, 9), (second.Width, second.Height));
        Assert.Equal((2f, 1f), (producer.Requests[1].Request.Altitude, producer.Requests[1].Request.Clouds));
        Assert.Null(computation.Pending);
    }

    /// <summary>Requested dimensions outside supported bounds share one normalized admission key.</summary>
    [Fact]
    public void EquivalentClampedDimensionsDoNotRepeatWork()
    {
        var producer = new ControlledProducer();
        using var computation = new AtmosphereComputation(producer.Start);
        computation.Update(Vector3.UnitY, 0, 0, 0, 0);
        var completed = producer.Complete(0);
        Assert.Equal((16, 8), (completed.Width, completed.Height));
        Assert.Same(completed, computation.Update(Vector3.UnitY, 0, 0, 16, 8));
        Assert.Single(producer.Requests);
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
    public void DisposalPreventsFurtherAdmissionAndPublication()
    {
        var producer = new ControlledProducer();
        var computation = new AtmosphereComputation(producer.Start);
        computation.Update(Vector3.UnitY, 0, 0, 128, 96);
        var task = computation.Pending!;
        Assert.False(producer.Requests[0].Token.IsCancellationRequested);
        computation.Dispose();
        computation.Dispose();
        Assert.True(producer.Requests[0].Token.IsCancellationRequested);
        Assert.Null(computation.Pending);
        // Complete after teardown to prove that even a producer which finishes cannot publish.
        producer.Complete(0);
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Throws<ObjectDisposedException>(() => computation.Update(Vector3.UnitY, 0, 0, 32, 24));
    }

    /// <summary>A failed completed build clears admission so the same inputs can be retried.</summary>
    [Fact]
    public void FailedProducerAllowsSameInputsToRetry()
    {
        var producer = new ControlledProducer();
        using var computation = new AtmosphereComputation(producer.Start);
        computation.Update(Vector3.UnitY, 0, 0, 32, 24);
        var error = new InvalidOperationException("Authored producer failure");
        producer.Requests[0].Completion.SetException(error);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => computation.Update(Vector3.UnitY, 0, 0, 32, 24)));
        Assert.Null(computation.Pending);
        Assert.Null(computation.Update(Vector3.UnitY, 0, 0, 32, 24));
        Assert.Equal(2, producer.Requests.Count);
    }
    #endregion

    #region Private
    /// <summary>Controls worker completion independently of the retained numerical integration tests.</summary>
    private sealed class ControlledProducer
    {
        #region Internal API
        /// <summary>Retains exact admitted requests, cancellation and independently controlled completion.</summary>
        internal List<(AtmosphereBuildRequest Request, CancellationToken Token, TaskCompletionSource<AtmosphereLighting> Completion)> Requests { get; } = [];

        /// <summary>Admits a frozen request without running numerical transport or using timed waits.</summary>
        internal Task<AtmosphereLighting> Start(AtmosphereBuildRequest request, CancellationToken token)
        {
            var completion = new TaskCompletionSource<AtmosphereLighting>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((request, token, completion));
            return completion.Task;
        }

        /// <summary>Publishes an identifiable immutable fixture value for the nominated admitted request.</summary>
        internal AtmosphereLighting Complete(int index)
        {
            var entry = Requests[index];
            var value = new AtmosphereLighting(entry.Request.Sun, Vector3.One, Vector3.Zero,
                Vector3.Zero, Vector3.One, System.Collections.Immutable.ImmutableArray<float>.Empty)
            { Width = entry.Request.Width, Height = entry.Request.Height };
            entry.Completion.SetResult(value);
            return value;
        }
        #endregion
    }
    #endregion
}
