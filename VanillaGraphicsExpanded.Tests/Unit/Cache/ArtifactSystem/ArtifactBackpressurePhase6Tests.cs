using System;

using VanillaGraphicsExpanded.Cache.ArtifactSystem;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache.Artifacts;

[Trait("Category", "Unit")]
public sealed class ArtifactBackpressurePhase6Tests
{
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
