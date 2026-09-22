using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldPartition;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.WorldPartition;

/// <summary>Reusable delayed-provider coverage proof across an entire publication interval and discontinuities.</summary>
public sealed class PartitionCoverageSweepTests
{
    #region Movement and publication
    /// <summary>Fractional movement has immediate complete demand and only acknowledged eventual readiness.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-16777216)]
    [InlineData(16777216)]
    public void IntervalSweepSeparatesDesiredFromReady(double anchor)
    {
        var limits = new PartitionLimits(1000, 1000, 1000, 1000, 100000);
        var coordinator = new PartitionCoordinator(limits);
        var provider = new PartitionProviderFixture();
        var layout = new PartitionLayout(new(16, 16, 16));
        long id = coordinator.Register("sweep", "test", layout, new(0, 0, 0), limits, provider);

        long tick = 0;
        for (int step = 0; step <= 64; step++)
        {
            // Sweep each axis through a full cell, in opposing directions to expose asymmetric rounding.
            var point = new PartitionPoint(anchor + step * .25, anchor - step * .25, anchor + step * .25);
            var plan = TraceGeometryCoverage.Plan(point, true, null, int.MaxValue);
            coordinator.SetSource(new(1, id, "test", point, plan.NearField!.Value));
            coordinator.Pump(tick++);
            AssertCoverage(coordinator, id, plan.NearField!.Value);
            var waiting = coordinator.Cells(id).Where(c => !c.Ready).ToArray();
            if (step == 0) Assert.NotEmpty(waiting);
            Assert.All(waiting, c => Assert.DoesNotContain(c.Key, provider.Visible));
            provider.CompleteAll();
            coordinator.Pump(tick++);
            Assert.All(coordinator.Cells(id), c => Assert.True(c.Ready));
            Assert.Equal(coordinator.Cells(id).Length, provider.Visible.Count);
        }
        // Dirty content disappears immediately, and a teleport abandons its delayed replacement.
        var dirty = coordinator.Cells(id)[0].Key;
        coordinator.Dirty(dirty);
        coordinator.Pump(tick++);
        Assert.DoesNotContain(dirty, provider.Visible);
        var abandoned = provider.Pending.Select(w => w.Request).ToArray();
        var teleported = new PartitionPoint(anchor + 1024.5, anchor - 1024.5, anchor + 1024.5);
        var destination = TraceGeometryCoverage.Plan(teleported, true, null, int.MaxValue);
        coordinator.SetSource(new(1, id, "test", teleported, destination.NearField!.Value));
        coordinator.Pump(tick++);
        AssertCoverage(coordinator, id, destination.NearField!.Value);
        Assert.All(coordinator.Cells(id), c => Assert.False(c.Ready));
        Assert.All(abandoned, r => Assert.True(r.Cancellation.IsCancellationRequested));
        provider.CompleteAll();
        coordinator.Pump(tick);
        Assert.All(coordinator.Cells(id), c => Assert.True(c.Ready));
        Assert.True(coordinator.Statistics(id).StaleCompletions > 0);
    }

    /// <summary>Compares demand to an independent half-open intersection oracle, including negative boundaries.</summary>
    private static void AssertCoverage(PartitionCoordinator coordinator, long id, in PartitionBounds bounds)
    {
        var expected = new HashSet<PartitionCoordinate>();
        for (long z = (long)Math.Floor(bounds.Min.Z / 16); z * 16 < bounds.Max.Z; z++)
        for (long y = (long)Math.Floor(bounds.Min.Y / 16); y * 16 < bounds.Max.Y; y++)
        for (long x = (long)Math.Floor(bounds.Min.X / 16); x * 16 < bounds.Max.X; x++) expected.Add(new(x, y, z));
        Assert.True(expected.SetEquals(coordinator.Cells(id).Where(c => c.Desired == PartitionResidency.Active).Select(c => c.Key.Coordinate)));
    }
    #endregion
}
