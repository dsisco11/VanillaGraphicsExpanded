using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn.WorldProbes;

/// <summary>Checks world probe trace service behavior and contract rules.</summary>
public sealed class WorldProbeTraceServiceTests
{
    #region Public API
    /// <summary>Verifies the service produces results for enqueued work behavior.</summary>
    [Fact]
    public async Task Service_ProducesResults_ForEnqueuedWork()
    {
        // Arrange
        var scene = new NeverHitScene();

        using var svc = new LumOnWorldProbeTraceService(
            scene,
            maxQueuedWorkItems: 8,
            tryClaim: (_, _) => true);

        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(0, 0, 0), new Vec3i(0, 0, 0), 0);
        // Act and Assert
        Assert.True(svc.TryEnqueue(new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 0,
            Request: request,
            ProbePosWorld: new Vector3d(0.5, 0.5, 0.5),
            MaxTraceDistanceWorld: 8,
            WorldProbeOctahedralTileSize: 16,
            WorldProbeAtlasTexelsPerUpdate: 8,
            EnableDirectionPIS: false,
            DirectionPISExploreFraction: 0.25f,
            DirectionPISExploreCount: -1,
            DirectionPISWeightEpsilon: 1e-6f)));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await svc.WaitForProgressAsync(timeout.Token);
        await svc.WaitForProgressAsync(timeout.Token);
        Assert.True(svc.TryDequeueResult(out var res));
        Assert.Equal(0, res.FrameIndex);
        Assert.True(res.Success);
    }

    /// <summary>A rejected claim wakes a registered observer even though there is no result to dequeue.</summary>
    /// <summary>Observing an already available completion repeatedly preserves it for the normal consumer.</summary>
    [Fact]
    public async Task RejectedClaimReleasesProgressWaiter()
    {
        // Arrange
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var service = new LumOnWorldProbeTraceService(new NeverHitScene(), 1,
            (_, _) => { entered.Set(); release.Wait(TestContext.Current.CancellationToken); return false; });
        try
        {
            // Act and Assert
            Assert.True(service.TryEnqueue(default));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            Assert.True(service.HasOutstandingWork);
            var pending = service.WaitForProgressAsync(TestContext.Current.CancellationToken);
            Assert.False(pending.IsCompleted);
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.False(service.HasOutstandingWork);
            Assert.False(service.TryDequeueResult(out _));
        }
        finally { release.Set(); }
    }

    /// <summary>Canceling a waiter preserves active work, while service cancellation releases remaining observers.</summary>
    [Fact]
    public async Task CancellationReleasesObserversWithoutConsumingResults()
    {
        // Arrange
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var service = new LumOnWorldProbeTraceService(new NeverHitScene(), 1,
            (_, _) => { entered.Set(); release.Wait(TestContext.Current.CancellationToken); return false; });
        try
        {
            // Act and Assert
            Assert.True(service.TryEnqueue(default));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            using var cancellation = new CancellationTokenSource();
            var canceled = service.WaitForProgressAsync(cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
            Assert.True(service.HasOutstandingWork);
            var pending = service.WaitForProgressAsync(TestContext.Current.CancellationToken);
            service.CancelOutstanding();
            await pending.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.False(service.HasOutstandingWork);
        }
        finally { release.Set(); }
    }

    #endregion
    #region Private
    /// <summary>Provides the never hit scene test helper.</summary>
    private sealed class NeverHitScene : IWorldProbeTraceScene
    {
        /// <summary>Provides the trace behavior used by this test helper.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
        {
            hit = default;
            return WorldProbeTraceOutcome.Sky;
        }
    }
    #endregion
}











