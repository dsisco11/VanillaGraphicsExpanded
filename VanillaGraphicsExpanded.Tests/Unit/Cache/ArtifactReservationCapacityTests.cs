using System;

using VanillaGraphicsExpanded.Cache.ArtifactSystem;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache;

/// <summary>Checks that artifact reservation capacity blocks and later admits work.</summary>
[Trait("Category", "Unit")]
public sealed class ArtifactReservationCapacityTests
{
    #region Public API
    /// <summary>Rejects reservations at capacity and admits one after a reservation is released.</summary>
    [Fact]
    public void ArtifactReservationPool_Enforces_Capacity_And_Allows_Deferral()
    {
        // Arrange: limit the pool to one in-flight reservation.
        var pool = new ArtifactReservationPool(capacity: 1);

        // Act: acquire and release the reservation around a second acquisition attempt.
        Assert.True(pool.TryAcquire(out ArtifactReservationToken t0));
        using (t0)
        {
            // Assert: capacity blocks the second reservation while the first is held.
            Assert.False(pool.TryAcquire(out _));
        }

        // Assert: releasing the first token makes the capacity available again.
        Assert.True(pool.TryAcquire(out ArtifactReservationToken t1));
        t1.Dispose();
    }
    #endregion
}
