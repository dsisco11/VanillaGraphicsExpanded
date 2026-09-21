using VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Minimal immutable snapshot provider shared by lighting, geometry, and lifecycle GPU scenarios.</summary>
internal sealed class LocalTraceVoxelProvider : IPartitionProvider
{
    private readonly LocalTraceGpuScene scene;
    private readonly LocalTraceMaterialRegistry materials;
    public Func<(int X, int Y, int Z), LocalTraceSourceCell>? CaptureCell { get; set; }
    private Action<PartitionCompletion>? lastCallback;
    private PartitionCompletion? lastCompletion;
    /// <summary>Copied payload shared read-only between capture, processing, and synchronous upload.</summary>
    private sealed record Payload(LocalTraceSourceCell[] Cells) : IPartitionSnapshot, IPartitionContent;
    /// <summary>Retains the production GPU backend and material palette.</summary>
    public LocalTraceVoxelProvider(LocalTraceGpuScene scene, LocalTraceMaterialRegistry materials) { this.scene = scene; this.materials = materials; }

    #region Provider operations
    /// <summary>Copies a complete cell in X/Z/Y order and claims its generation before dispatch.</summary>
    public PartitionCapture Capture(PartitionRequest request)
    {
        if (CaptureCell == null || !scene.ClaimCell(request)) return new(PartitionContentStatus.MissingDependencies, null);
        int size = scene.CellSize;
        var cells = new LocalTraceSourceCell[size * size * size];
        var c = request.Key.Coordinate;
        for (int y = 0; y < size; y++) for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            cells[(y * size + z) * size + x] = CaptureCell((checked((int)c.X * size + x), checked((int)c.Y * size + y), checked((int)c.Z * size + z)));
        return new(PartitionContentStatus.Supported, new Payload(cells));
    }
    /// <summary>Acknowledges the immutable captured content without a separate worker algorithm.</summary>
    public void Dispatch(PartitionRequest request, IPartitionSnapshot snapshot, Action<PartitionCompletion> complete)
    {
        lastCallback = complete;
        lastCompletion = new(request, snapshot, (Payload)snapshot, ((Payload)snapshot).Cells.Length * 8L, PartitionContentStatus.Supported);
        complete(lastCompletion);
    }
    /// <summary>Snapshot inputs change only through explicit fixture invalidation.</summary>
    public bool DependenciesValid(PartitionRequest request, IPartitionSnapshot snapshot) => true;
    /// <summary>Uses the production coherent geometry, light, material, and readiness upload.</summary>
    public bool Publish(PartitionCompletion completion) => scene.PublishCell(completion.Request, ((Payload)completion.Content!).Cells, materials);
    /// <summary>Content already resides on the GPU before activation.</summary>
    public bool SetActive(in PartitionCellKey key, bool active) => true;
    /// <summary>Makes stale content unavailable immediately.</summary>
    public void Invalidate(in PartitionCellKey key) => scene.InvalidateCell(key);
    /// <summary>Releases the backend's logical slot owner.</summary>
    public void Retire(in PartitionCellKey key) => scene.RetireCell(key);
    /// <summary>Delivers a duplicate old callback independently from the publication owner.</summary>
    public Action CaptureCompletionReplay()
    {
        var completion = lastCompletion ?? throw new InvalidOperationException("No completed capture.");
        var callback = lastCallback!;
        return () => callback(completion);
    }
    #endregion
}
