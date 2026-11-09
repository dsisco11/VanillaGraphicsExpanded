using System.Runtime.ExceptionServices;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Verifies deliberate worker-gate notifications without a graphics context or timing sleeps.</summary>
public sealed class RuntimeProbeWorldNotificationTests
{
    #region Public API
    /// <summary>Entry wakes registered observers and remains observable without releasing held traversal.</summary>
    [Fact]
    public async Task EnteredWorkerRemainsHeldAfterRepeatedObservation()
    {
        // Arrange
        using var world = new RuntimeProbeWorld(new Block { BlockId = 1 });
        Task<Block>? read = null;
        Exception? primaryFailure = null;
        try
        {
            // Act
            world.HoldWorker();
            var entry = world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken);
            Assert.False(entry.IsCompleted);
            // A dedicated thread guarantees this is a worker read even under a constrained test scheduler.
            read = Task.Factory.StartNew(() => world.Accessor.GetMostSolidBlock(new BlockPos(0, 32, 0)),
                TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            // Assert
            await entry.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.True(world.WorkerWaiting);
            Assert.True(world.WorkerHeld);
            Assert.True(world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);
            Assert.False(read.IsCompleted);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
        }
        finally
        {
            world.ReleaseWorker();
        }

        // Assert worker cleanup independently so a failed entry assertion cannot leave the helper thread blocked.
        if (read is not null)
        {
            try
            {
                // Observe worker completion after releasing the deliberate hold, even if an earlier assertion failed.
                await read.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception cleanupFailure)
            {
                if (primaryFailure is null)
                {
                    throw;
                }

                throw new AggregateException("The worker gate test failed and its worker did not complete cleanly.",
                    primaryFailure, cleanupFailure);
            }
        }

        if (primaryFailure is not null)
        {
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }
    }

    /// <summary>Release before entry wakes subscribers and a new hold receives a fresh incomplete notification.</summary>
    [Fact]
    public async Task ReleaseBeforeEntryWakesObserverAndNextHoldResetsIt()
    {
        // Arrange
        using var world = new RuntimeProbeWorld(new Block { BlockId = 1 });
        // Act
        world.HoldWorker();
        var entry = world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken);
        world.ReleaseWorker();
        // Assert
        await entry.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.False(world.WorkerWaiting);
        Assert.True(world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);
        // Act: a new hold must publish a new, incomplete entry notification.
        world.HoldWorker();
        var next = world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken);
        // Assert
        Assert.False(next.IsCompleted);
        // Act: disposal releases a worker held after the new notification.
        world.Dispose();
        // Assert
        await next.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    /// <summary>Canceling one observer leaves the deliberate hold and other observers intact.</summary>
    [Fact]
    public async Task ObserverCancellationDoesNotReleaseWorkerGate()
    {
        using var world = new RuntimeProbeWorld(new Block { BlockId = 1 });
        // Arrange
        // Act
        world.HoldWorker();
        using var cancellation = new CancellationTokenSource();
        var canceled = world.WaitForWorkerEntryAsync(cancellation.Token);
        var retained = world.WaitForWorkerEntryAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.True(world.WorkerHeld);
        Assert.False(retained.IsCompleted);
        // Act
        world.ReleaseWorker();
        // Assert
        await retained.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }
    #endregion
}
