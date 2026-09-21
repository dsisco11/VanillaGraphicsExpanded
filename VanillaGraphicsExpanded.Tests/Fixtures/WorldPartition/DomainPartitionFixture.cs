using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Fixtures.WorldPartition;

/// <summary>Reusable external backend with explicitly controlled world-zero coverage.</summary>
internal sealed class DomainPartitionFixture : IPartitionResidencyBackend
{
    public PartitionCoordinator Coordinator { get; }
    public PartitionCellKey Key { get; private set; }
    private readonly long instance;

    #region Controlled coverage
    /// <summary>Shares limits with other fixtures when testing cross-partition contention.</summary>
    public DomainPartitionFixture(PartitionCoordinator? coordinator = null)
    {
        Coordinator = coordinator ?? new(new(8, 8, 8, 8, 1024));
        instance = Coordinator.Register("domain", "test", new(new(32, 32, 32)), new(0, 0, 0), new(8, 8, 8, 8, 1024), this);
        SetWindow(0);
    }

    /// <summary>Replaces the exact one-cell source without changing its fixed layout.</summary>
    public void SetWindow(long x)
    {
        Key = new(instance, "test", new(x, 0, 0));
        var bounds = new PartitionBounds(new(x * 32d, 0, 0), new((x + 1d) * 32, 32, 32));
        Coordinator.SetSource(new(0, instance, "test", bounds.Min, bounds));
        Coordinator.RefreshCoverage(instance);
    }
    #endregion

    #region Backend acknowledgements
    /// <summary>Records no lighting interpretation at the residency boundary.</summary>
    public bool SetActive(in PartitionCellKey key, bool active) => true;
    /// <summary>Emulates immediate backend invalidation.</summary>
    public void Invalidate(in PartitionCellKey key) { }
    /// <summary>Emulates immediate resource retirement.</summary>
    public void Retire(in PartitionCellKey key) { }
    #endregion
}
