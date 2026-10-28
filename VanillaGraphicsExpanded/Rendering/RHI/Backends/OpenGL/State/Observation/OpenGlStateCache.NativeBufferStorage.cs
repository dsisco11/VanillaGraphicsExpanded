using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Withdraws externally changed buffer storage until explicit descriptor acquisition.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Marks the selected live allocation uncertain after a completed storage command without querying it.</summary>
    internal static void ObserveBufferStorage(int selector, bool named)
    {
        var cache = Current;
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true && cache.resourceOperation is null) return;
        int id = named ? selector : cache.GetBoundBuffer((BufferTarget)selector);
        cache.InterruptNativeResourceOperation(GpuResourceKind.Buffer, id);
        if (manager?.CanObserveNativeMetadata != true) return;
        var entry = manager.FindObservedResource(GpuResourceKind.Buffer, id) as GpuResourceMetadataEntry<GpuBufferMetadata>;
        if (entry is null || InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage)) return;
        // A returned storage call does not prove allocation acceptance. Recovery belongs to the next consumer.
        entry.Reader ??= new GpuBufferMetadataReader();
        entry.BeginRefresh(GpuMetadataChange.Storage);
    }
    #endregion
}
