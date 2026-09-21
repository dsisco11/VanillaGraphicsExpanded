using VanillaGraphicsExpanded.Tests.Fixtures.WorldPartition;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.WorldPartition;

/// <summary>Missing dependencies use bounded polling without delaying explicit dirty notifications.</summary>
public sealed class PartitionRetryBackoffTests
{
    #region Retry scheduling
    /// <summary>Persistent missing data cannot consume the capture budget every update and eventually recovers.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingDependencyBacksOffAndRecovers(bool notify)
    {
        var limits = new PartitionLimits(27, 64, 64, 32, 100000);
        var coordinator = new PartitionCoordinator(limits);
        var provider = new PartitionProviderFixture { Missing = true, AutoComplete = true };
        long id = coordinator.Register("retry", "test", new(new(16, 16, 16)), new(0, 0, 0), limits, provider);
        coordinator.SetSource(new(1, id, "test", new(), new(new(), new(48, 48, 48))));
        for (int tick = 0; tick < 200; tick++) coordinator.Pump(tick);
        Assert.InRange(provider.Captures.Count, 27, 27 * 10);
        Assert.Empty(provider.Publications);
        provider.Missing = false;
        if (notify)
        {
            foreach (var cell in coordinator.Cells(id)) coordinator.Dirty(cell.Key);
            coordinator.Pump(200);
        }
        else
        {
            for (int tick = 200; tick <= 264; tick++) coordinator.Pump(tick);
        }
        Assert.Equal(27, provider.Publications.Count);
        Assert.All(coordinator.Cells(id), cell => Assert.True(cell.Ready));
    }
    #endregion
}
