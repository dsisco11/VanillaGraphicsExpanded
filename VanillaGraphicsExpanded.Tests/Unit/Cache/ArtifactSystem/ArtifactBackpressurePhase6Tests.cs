using System;
using System.Threading;

using VanillaGraphicsExpanded.Cache.ArtifactSystem;
using VanillaGraphicsExpanded.PBR.Materials.Async;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache.Artifacts;

[Trait("Category", "Unit")]
public sealed class ArtifactBackpressurePhase6Tests
{
    [Fact]
    public void MaterialAtlasDiskCacheIoQueue_DoesNotDrop_Writes()
    {
        const int total = 50;

        using var countdown = new CountdownEvent(total);
        int executed = 0;

        for (int i = 0; i < total; i++)
        {
            bool queued = MaterialAtlasDiskCacheIoQueue.TryQueue(() =>
            {
                Interlocked.Increment(ref executed);
                countdown.Signal();
            });

            Assert.True(queued);
        }

        Assert.True(countdown.Wait(millisecondsTimeout: 5_000, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(total, Volatile.Read(ref executed));
    }

    [Fact]
    public void ArtifactReservationPool_Enforces_Capacity_And_Allows_Deferral()
    {
        var pool = new ArtifactReservationPool(capacity: 1);

        Assert.True(pool.TryAcquire(out ArtifactReservationToken t0));
        using (t0)
        {
            Assert.False(pool.TryAcquire(out _));
        }

        Assert.True(pool.TryAcquire(out ArtifactReservationToken t1));
        t1.Dispose();
    }
}
