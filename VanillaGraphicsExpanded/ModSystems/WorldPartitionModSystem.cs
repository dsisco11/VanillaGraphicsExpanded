using VanillaGraphicsExpanded.WorldPartition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Owns the shared spatial coordinator for rendering partitions.</summary>
public sealed class WorldPartitionModSystem : ModSystem
{
    private PartitionCoordinator? coordinator;
    private long tick;
    private ICoreClientAPI? client;
    internal bool RecordDiagnostics { get; set; }

    /// <summary>Creates the coordinator lazily on the render thread which owns its providers.</summary>
    internal PartitionCoordinator GetCoordinator() => coordinator ??= new(new(16384, 256, 128, 128, 32L * 1024 * 1024));

    /// <summary>Services all registered providers once per near-field geometry render update.</summary>
    internal void Pump() => GetCoordinator().Pump(++tick);

    /// <summary>Pairs all primary-world registrations with the client world lifetime.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        client = api;
        api.Event.LeaveWorld += UnloadPartitions;
    }

    /// <summary>Invalidates outstanding registrations before another world can reuse their coordinates.</summary>
    private void UnloadPartitions() => coordinator?.UnloadWorld("primary");

    /// <summary>Removes the world-lifetime subscription; providers retire their GPU resources on world leave.</summary>
    public override void Dispose()
    {
        if (client != null) client.Event.LeaveWorld -= UnloadPartitions;
        client = null;
        base.Dispose();
    }

    /// <summary>Spatial rendering partitions only load on clients.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
}
