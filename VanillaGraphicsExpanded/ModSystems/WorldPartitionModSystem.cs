using VanillaGraphicsExpanded.LumOn.WorldCells;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class WorldPartitionModSystem : ModSystem
{
    internal WorldPartitionSystem Partition { get; } = new();

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
}
