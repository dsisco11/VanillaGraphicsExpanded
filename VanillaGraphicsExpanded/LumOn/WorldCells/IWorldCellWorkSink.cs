namespace VanillaGraphicsExpanded.LumOn.WorldCells;

/// <summary>
/// Provides a stable surface for <see cref="IWorldCell"/> instances to enqueue themselves into system-owned work queues.
/// Cells decide which queues to target; systems own the queue implementations.
/// </summary>
internal interface IWorldCellWorkSink
{
    long NowTick { get; }

    void Upsert(WorldCellKey key, WorldCellWorkQueue queue, float priority);

    void Remove(WorldCellKey key, WorldCellWorkQueue queue);

    void SetCooldown(WorldCellKey key, long tick);
}
