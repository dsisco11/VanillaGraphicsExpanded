namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>
/// No-op <see cref="IWorldCellWorkSink"/> implementation.
/// Useful as a safe default when a system does not support a given <see cref="WorldCellWorkQueue"/>.
/// </summary>
internal sealed class NullWorldCellWorkSink : IWorldCellWorkSink
{
    public static readonly NullWorldCellWorkSink Instance = new();

    private NullWorldCellWorkSink()
    {
    }

    public long NowTick => 0;

    public void Upsert(WorldCellKey key, WorldCellWorkQueue queue, float priority)
    {
        _ = key;
        _ = queue;
        _ = priority;
    }

    public void Remove(WorldCellKey key, WorldCellWorkQueue queue)
    {
        _ = key;
        _ = queue;
    }

    public void SetCooldown(WorldCellKey key, long tick)
    {
        _ = key;
        _ = tick;
    }
}
