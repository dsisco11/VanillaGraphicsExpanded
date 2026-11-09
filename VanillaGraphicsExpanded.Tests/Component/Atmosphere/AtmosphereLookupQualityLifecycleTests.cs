using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Verifies quality admission and bounded multiple-scattering resolution.</summary>
public sealed class AtmosphereLookupQualityLifecycleTests
{
    #region Public API
    /// <summary>Completed admitted work is published before the latest quality starts its replacement.</summary>
    [Fact]
    public void AsyncCompletionAdmitsQualityOnlyChange()
    {
        // Arrange: control completion so this policy test does not run numerical integration.
        var producer = new ControlledProducer();
        using var computation = new AtmosphereComputation(producer.Start);

        // Act and Assert: the first three updates retain the admitted quality-zero task; completing it
        // publishes that snapshot and admits quality one, whose completion then becomes the stable result.
        Assert.Null(computation.Update(Vector3.UnitY, 2, 0, 16, 8));
        Assert.Equal(new AtmosphereBuildRequest(Vector3.UnitY, 2, 0, 16, 8, 0, .1f), producer.Requests[0].Request);
        var firstPending = Assert.IsAssignableFrom<Task<AtmosphereLighting>>(computation.Pending);
        Assert.Null(computation.Update(Vector3.UnitY, 2, 0, 16, 8));
        Assert.Null(computation.Update(Vector3.UnitY, 2, 0, 16, 8, quality: 1));
        Assert.Same(firstPending, computation.Pending);
        Assert.False(firstPending.IsCompleted);
        Assert.Single(producer.Requests);

        var first = producer.Complete(0);
        Assert.Same(first, computation.Update(Vector3.UnitY, 2, 0, 16, 8, quality: 1));
        Assert.Equal(new AtmosphereBuildRequest(Vector3.UnitY, 2, 0, 16, 8, 1, .1f), producer.Requests[1].Request);
        Assert.NotNull(computation.Pending);
        Assert.Equal(2, producer.Requests.Count);

        var second = producer.Complete(1);
        Assert.NotSame(first, second);
        Assert.Same(second, computation.Update(Vector3.UnitY, 2, 0, 16, 8, quality: 1));
        Assert.Null(computation.Pending);
        Assert.Null(computation.Update(Vector3.UnitY, 2, 0, 16, 8, quality: 1));
        Assert.Equal(2, producer.Requests.Count);
    }
    #endregion

    #region Private
    /// <summary>Controls completion independently so the policy test avoids numerical construction.</summary>
    private sealed class ControlledProducer
    {
        /// <summary>Retains each frozen request with its independently controlled completion source.</summary>
        internal List<(AtmosphereBuildRequest Request, TaskCompletionSource<AtmosphereLighting> Completion)> Requests { get; } = [];

        /// <summary>Records an admitted request and returns its incomplete completion dependency.</summary>
        internal Task<AtmosphereLighting> Start(AtmosphereBuildRequest request, CancellationToken token)
        {
            var completion = new TaskCompletionSource<AtmosphereLighting>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((request, completion));
            return completion.Task;
        }

        /// <summary>Completes the selected request with a unique immutable publication.</summary>
        internal AtmosphereLighting Complete(int index)
        {
            var entry = Requests[index];
            var value = new AtmosphereLighting(entry.Request.Sun, Vector3.One, Vector3.Zero,
                Vector3.Zero, Vector3.One, System.Collections.Immutable.ImmutableArray<float>.Empty)
            { Width = entry.Request.Width, Height = entry.Request.Height };
            entry.Completion.SetResult(value);
            return value;
        }
    }
    #endregion
}
