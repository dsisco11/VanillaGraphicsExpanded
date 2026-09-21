using VanillaGraphicsExpanded.LumOn.WorldCells;
using VanillaGraphicsExpanded.WorldPartition;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Owns the shared spatial coordinator and the legacy registry during consumer migration.</summary>
public sealed class WorldPartitionModSystem : ModSystem
{
    internal WorldPartitionSystem Partition { get; } = new();
    private PartitionCoordinator? coordinator;
    private long tick;

    /// <summary>Creates the coordinator lazily on the render thread which owns its providers.</summary>
    internal PartitionCoordinator GetCoordinator() => coordinator ??= new(new(16384, 256, 128, 128, 32L * 1024 * 1024));

    /// <summary>Services all registered providers once per near-field geometry render update.</summary>
    internal void Pump() => GetCoordinator().Pump(++tick);

    /// <summary>Spatial rendering partitions only load on clients.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
}
