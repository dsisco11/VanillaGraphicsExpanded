namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes observed vertex-array revisions without maintaining a second native layout mirror.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Invalidates affected helper knowledge and publishes the empty canonical schema after native mutation.</summary>
    internal static void ObserveVertexMetadata(int selector, bool named, bool elementBinding)
    {
        var cache = Current;
        int vao = named ? selector : cache.GetCurrentVao();
        cache.InterruptNativeResourceOperation(GpuResourceKind.VertexArray, vao);
        cache.InvalidateVertexConfiguration(vao);
        if (elementBinding) cache.elementArrayBufferByVao.Remove(vao);
        var manager = RHIResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true) return;
        var entry = manager.FindObservedResource(GpuResourceKind.VertexArray, vao)
            as GpuResourceMetadataEntry<GpuVertexArrayMetadata>;
        if (entry is not { IsRealized: true, IsUsable: true }) return;
        // There are no native fields to acquire. Publication itself owns dependent notification and reentry exclusion.
        manager.PublishMetadata(entry, default(GpuVertexArrayMetadata), GpuMetadataChange.Metadata);
    }
    #endregion
}
