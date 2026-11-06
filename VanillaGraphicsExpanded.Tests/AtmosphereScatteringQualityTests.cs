using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies quality admission and bounded multiple-scattering resolution.</summary>
public sealed class AtmosphereScatteringQualityTests
{
    #region Public API
    #region Quality admission
    /// <summary>Completed admitted work is published before the latest quality starts its replacement.</summary>
    [Fact]
    public void AsyncCompletionAdmitsQualityOnlyChange()
    {
        // Arrange: control completion so this policy test does not run numerical integration.
        var producer = new ControlledProducer();
        using var computation = new AtmosphereComputation(producer.Start);

        // Act: keep quality zero pending while requesting quality one, then consume each completion.
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

    #region Bounds and cancellation
    /// <summary>Quality increases spatial dimensions and quadrature budgets linearly with bounded endpoints.</summary>
    [Theory]
    [InlineData(-1, 32, 16, 128, 24, 12)]
    [InlineData(0, 32, 16, 128, 24, 12)]
    [InlineData(1, 64, 32, 256, 48, 24)]
    [InlineData(2, 96, 48, 384, 72, 36)]
    [InlineData(3, 128, 64, 512, 96, 48)]
    [InlineData(4, 128, 64, 512, 96, 48)]
    public void QualitySelectsCompleteBudget(int quality, int width, int height, int directions, int rays, int light)
    {
        Assert.Equal(new AtmosphereScatteringBudget(width, height, directions, rays, light),
            AtmosphereScatteringBudget.FromQuality(quality));
    }

    /// <summary>Solar optical-depth refinement affects source radiance without altering isotropic feedback.</summary>
    [Fact]
    public void LightQuadratureRefinesSourceOnly()
    {
        Vector3 direction = Vector3.Normalize(new Vector3(1, .1f, 0));
        Vector3 sun = Vector3.Normalize(new Vector3(1, .03f, 0));
        var coarse = AtmosphereModel.MultipleScatteringTransfer(direction, sun, 2, 1, .1f, 48, 2);
        var fine = AtmosphereModel.MultipleScatteringTransfer(direction, sun, 2, 1, .1f, 48, 96);
        Assert.Equal(coarse.Feedback, fine.Feedback);
        Assert.True(Vector3.Distance(coarse.Source, fine.Source) > 1e-6f);
        for (int channel = 0; channel < 3; channel++)
            Assert.True(float.IsFinite(fine.Source[channel]) && fine.Source[channel] > 0);
    }

    /// <summary>The maximum quality is admitted but cancellation stops integration; larger tables are rejected.</summary>
    [Fact]
    public void MaximumTableHonorsCancellationAndRejectsLargerDimensions()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => AtmosphereMultipleScattering.Build(1, cancellation.Token,
            sunSamples: 128, altitudeSamples: 64, directionSamples: 1024, raySamples: 192, lightSamples: 96));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, sunSamples: 129));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, altitudeSamples: 65));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, lightSamples: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMultipleScattering.Build(1, lightSamples: 97));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Controls completion independently so the policy test avoids numerical construction.</summary>
    private sealed class ControlledProducer
    {
        #region Internal API
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
        #endregion
    }
    #endregion
}
