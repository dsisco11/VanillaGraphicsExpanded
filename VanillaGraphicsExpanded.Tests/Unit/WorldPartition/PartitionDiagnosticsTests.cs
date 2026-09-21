using VanillaGraphicsExpanded.Tests.Fixtures.WorldPartition;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.WorldPartition;

/// <summary>Diagnostic observations retain source identity and separate demand from acknowledged readiness.</summary>
public sealed class PartitionDiagnosticsTests
{
    #region Readiness and backlog
    /// <summary>Evicting a ready speculative cell begins a new wait interval instead of counting its prior ready lifetime.</summary>
    [Fact]
    public void EvictedReadyCellRestartsPublicationWait()
    {
        var limits = new PartitionLimits(2, 100, 100, 100, 10000);
        var coordinator = new PartitionCoordinator(limits);
        var provider = new PartitionProviderFixture { AutoComplete = true };
        long id = coordinator.Register("terrain", "world", new(new(16, 16, 16)), new(1, 1, 0), limits, provider);
        coordinator.SetSource(new(1, id, "world", new(1, 1, 1), new(new(1, 1, 1), new(16, 2, 2))));
        coordinator.Pump(0);
        Assert.True(coordinator.Cells(id).Single(c => c.Key.Coordinate.X == 1).Ready);
        coordinator.SetSource(new(2, id, "world", new(161, 1, 1), new(new(161, 1, 1), new(162, 2, 2))));
        coordinator.Pump(100);
        Assert.False(coordinator.Cells(id).Single(c => c.Key.Coordinate.X == 1).Ready);
        coordinator.RemoveSource(id, 2);
        coordinator.SetSource(new(1, id, "world", new(15, 1, 1), new(new(15, 1, 1), new(17, 2, 2))));
        provider.AutoComplete = false;
        coordinator.Pump(102);
        Assert.Equal(2, Assert.Single(coordinator.Diagnostics()).OldestRequiredWaitTicks);
        provider.CompleteAll();
        coordinator.Pump(104);
        var diagnostics = Assert.Single(coordinator.Diagnostics());
        Assert.Equal(2, diagnostics.RequiredReady);
        Assert.Equal(4, diagnostics.MaximumPublicationWaitTicks);
    }

    /// <summary>Retries cannot reset the reported age of missing required content.</summary>
    [Fact]
    public void MissingDependenciesAccumulateWaitAndRecover()
    {
        var limits = new PartitionLimits(100, 100, 100, 100, 10000);
        var coordinator = new PartitionCoordinator(limits);
        var provider = new PartitionProviderFixture { Missing = true };
        long id = coordinator.Register("terrain", "world", new(new(16, 16, 16)), new(0, 0, 0), limits, provider);
        var source = new PartitionSource(1, id, "world", new(-.5, 1, 1), new(new(-1, 0, 0), new(1, 1, 1)));
        coordinator.SetSource(source);
        coordinator.Pump(0);
        coordinator.Pump(5);
        var missing = Assert.Single(coordinator.Diagnostics());
        Assert.Equal(source, Assert.Single(missing.Sources));
        Assert.Equal("terrain", missing.Name);
        Assert.Equal(2, missing.Statistics.Required);
        Assert.Equal(0, missing.RequiredReady);
        Assert.Equal(5, missing.OldestRequiredWaitTicks);
        Assert.True(missing.Statistics.Retries >= 2);
        provider.Missing = false;
        coordinator.Pump(6);
        Assert.Equal(0, coordinator.Statistics(id).InFlight);
        coordinator.Pump(7);
        Assert.Equal(2, coordinator.Statistics(id).InFlight);
        provider.CompleteAll();
        coordinator.Pump(8);
        var ready = Assert.Single(coordinator.Diagnostics());
        Assert.Equal(2, ready.RequiredReady);
        Assert.Equal(0, ready.OldestRequiredWaitTicks);
        Assert.Equal(2, ready.PublicationCount);
        Assert.Equal(8, ready.MeanPublicationWaitTicks);
        Assert.Equal(8, ready.MaximumPublicationWaitTicks);
        // Earlier observations remain detached when current state changes.
        Assert.All(missing.Cells, cell => Assert.False(cell.Ready));
        coordinator.Dirty(ready.Cells[0].Key);
        Assert.Equal(1, Assert.Single(coordinator.Diagnostics()).RequiredReady);
    }
    #endregion
}
