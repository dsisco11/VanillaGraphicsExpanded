using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Tests.Component.WorldPartition.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Component.WorldPartition;

/// <summary>Verifies coordinator coverage, source union, retention, and delayed publication.</summary>
public sealed class PartitionCoverageTests
{
    #region Public API
    /// <summary>Fractional source motion preserves requested cells until delayed provider completion.</summary>
    [Theory]
    [InlineData(16, 0)]
    [InlineData(32, -64)]
    [InlineData(16, 16777216)]
    public void FullIntervalSweep(int extent, double anchor)
    {
        // Arrange: register a coordinator with a controlled provider that completes only when asked.
        var provider = new PartitionProviderFixture();
        var coordinator = new PartitionCoordinator(new(1000, 1000, 1000, 1000, 10000));
        long id = coordinator.Register("geometry", "world", new(new(extent, extent, extent)), new(0, 0, 0), new(1000, 1000, 1000, 1000, 10000), provider);

        // Act and assert: each quarter-unit move preserves coverage until completion publishes all requested cells.
        for (int step = 0; step <= extent * 4; step++)
        {
            double x = anchor + step * 0.25;
            coordinator.SetSource(new(1, id, "world", new(x, 0, 0), new(new(x - 32, 0, 0), new(x + 32, 1, 1))));
            coordinator.Pump(step * 2);
            PartitionCellInfo[] cells = coordinator.Cells(id);
            long first = (long)Math.Floor((x - 32) / extent);
            long last = (long)Math.Ceiling((x + 32) / extent) - 1;
            Assert.Equal(Enumerable.Range(0, checked((int)(last - first + 1))).Select(i => first + i), cells.Select(c => c.Key.Coordinate.X).Order());
            Assert.All(cells, c => Assert.Equal(PartitionResidency.Active, c.Desired));
            if (step == 0) Assert.All(cells, c => Assert.False(c.Ready));
            provider.CompleteAll();
            coordinator.Pump(step * 2 + 1);
            Assert.All(coordinator.Cells(id), c => Assert.True(c.Ready));
        }
    }

    /// <summary>Same-category instances and source identities remain scoped by partition and world.</summary>
    [Fact]
    public void InstancesSourcesAndWorldsRemainIndependent()
    {
        // Arrange: share a controlled provider across two instances and distinct worlds.
        var coordinator = new PartitionCoordinator(new(100, 100, 100, 100, 10000));
        var provider = new PartitionProviderFixture { AutoComplete = true };
        long a = coordinator.Register("same", "a", new(new(16, 16, 16)), new(0, 0, 0), new(100, 100, 100, 100, 10000), provider);
        long b = coordinator.Register("same", "b", new(new(8, 8, 8)), new(0, 0, 0), new(100, 100, 100, 100, 10000), provider);

        // Act: submit overlapping source ranges to one instance and an independent range to the other.
        coordinator.SetSource(new(1, a, "a", new(), new(new(), new(16, 1, 1))));
        coordinator.SetSource(new(2, a, "a", new(), new(new(15, 0, 0), new(33, 1, 1))));
        coordinator.SetSource(new(1, b, "b", new(), new(new(), new(16, 1, 1))));
        coordinator.Pump(0);

        // Act and Assert: removing a source and unloading its world leave the other partition independent.
        Assert.Equal(3, coordinator.Cells(a).Length);
        Assert.Equal(2, coordinator.Cells(b).Length);
        coordinator.RemoveSource(a, 2);
        coordinator.Pump(1);
        Assert.Single(coordinator.Cells(a));
        Assert.Equal(2, coordinator.Cells(b).Length);
        Assert.Throws<ArgumentException>(() => coordinator.SetSource(new(3, a, "b", new(), new(new(), new(1, 1, 1)))));
        coordinator.UnloadWorld("a");
        Assert.Equal(2, coordinator.Cells(b).Length);
        Assert.All(provider.Visible, key => Assert.Equal(b, key.Instance));
    }

    /// <summary>Boundary oscillation retains ready neighbors without delaying new required cells.</summary>
    [Fact]
    public void RetentionExpiresAndStableSourcesAvoidReevaluation()
    {
        // Arrange: complete provider work immediately to isolate coordinator retention behavior.
        var provider = new PartitionProviderFixture { AutoComplete = true };
        var coordinator = new PartitionCoordinator(new(10, 10, 10, 10, 100));
        long id = coordinator.Register("p", "w", new(new(16, 16, 16)), new(0, 16, 2), new(10, 10, 10, 10, 100), provider);
        PartitionSource source = new(1, id, "w", new(), new(new(15, 0, 0), new(16, 1, 1)));

        // Act and assert: a boundary shift retains the old incarnation temporarily, then removes it.
        coordinator.SetSource(source); coordinator.Pump(0);
        long incarnation = coordinator.Cells(id)[0].Incarnation;
        coordinator.SetSource(source with { Required = new(new(16, 0, 0), new(17, 1, 1)) }); coordinator.Pump(1);
        Assert.Equal(2, coordinator.Cells(id).Length);
        Assert.Equal(PartitionResidency.Loaded, coordinator.Cells(id).Single(x => x.Incarnation == incarnation).Desired);
        coordinator.SetSource(source); coordinator.Pump(2);
        Assert.Equal(incarnation, coordinator.Cells(id).Single(x => x.Key.Coordinate.X == 0).Incarnation);
        long evaluations = coordinator.Statistics(id).CoverageEvaluations;
        coordinator.SetSource(source); coordinator.Pump(5);
        Assert.Equal(evaluations, coordinator.Statistics(id).CoverageEvaluations);
        Assert.Single(coordinator.Cells(id));
    }
    #endregion
}










