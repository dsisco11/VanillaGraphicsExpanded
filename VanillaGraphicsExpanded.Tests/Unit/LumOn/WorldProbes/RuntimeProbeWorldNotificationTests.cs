using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Verifies deliberate worker-gate notifications without a graphics context or timing sleeps.</summary>
public sealed class RuntimeProbeWorldNotificationTests
{
    #region Milestone notification
    /// <summary>Entry wakes registered observers and remains observable without releasing held traversal.</summary>
    [Fact]
    public async Task EnteredWorkerRemainsHeldAfterRepeatedObservation()
    {
        using var world = new RuntimeProbeWorld(new Block { BlockId = 1 });
        world.HoldWorker();
        var entry = world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken);
        Assert.False(entry.IsCompleted);
        // A dedicated thread guarantees this is a worker read even under a constrained test scheduler.
        var read = Task.Factory.StartNew(() => world.Accessor.GetMostSolidBlock(new BlockPos(0, 32, 0)),
            TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await entry.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.True(world.WorkerWaiting);
            Assert.True(world.WorkerHeld);
            Assert.True(world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);
            Assert.False(read.IsCompleted);
        }
        finally { world.ReleaseWorker(); }
        await read.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    /// <summary>Release before entry wakes subscribers and a new hold receives a fresh incomplete notification.</summary>
    [Fact]
    public async Task ReleaseBeforeEntryWakesObserverAndNextHoldResetsIt()
    {
        using var world = new RuntimeProbeWorld(new Block { BlockId = 1 });
        world.HoldWorker();
        var entry = world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken);
        world.ReleaseWorker();
        await entry.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.False(world.WorkerWaiting);
        Assert.True(world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);
        world.HoldWorker();
        var next = world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken);
        Assert.False(next.IsCompleted);
        world.Dispose();
        await next.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    /// <summary>Canceling one observer leaves the deliberate hold and other observers intact.</summary>
    [Fact]
    public async Task ObserverCancellationDoesNotReleaseWorkerGate()
    {
        using var world = new RuntimeProbeWorld(new Block { BlockId = 1 });
        world.HoldWorker();
        using var cancellation = new CancellationTokenSource();
        var canceled = world.WaitForWorkerEntryAsync(cancellation.Token);
        var retained = world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.True(world.WorkerHeld);
        Assert.False(retained.IsCompleted);
        world.ReleaseWorker();
        await retained.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }
    #endregion
}
