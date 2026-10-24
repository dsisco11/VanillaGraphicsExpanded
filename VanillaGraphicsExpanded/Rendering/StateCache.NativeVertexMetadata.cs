namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes vertex-array configuration revisions without introducing a native layout mirror.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Captures an existing VAO registration and withdraws helper assumptions before mutation.</summary>
    internal static NativeVertexMetadata BeginNativeVertexMetadata(int selector, bool named, bool elementBinding)
    {
        var cache = Current;
        int vao = named ? selector : cache.GetCurrentVao();
        if (elementBinding) cache.elementArrayBufferByVao.Remove(vao);
        var manager = GpuResourceManagerSystem.CurrentManager;
        var entry = manager?.FindObservedResource(GpuResourceKind.VertexArray, vao)
            as GpuResourceMetadataEntry<GpuVertexArrayMetadata>;
        entry?.BeginRefresh(GpuMetadataChange.Metadata);
        return new(cache, vao, elementBinding, entry);
    }
    #endregion

    /// <summary>Retains the original VAO entry and invalidates only affected binding knowledge.</summary>
    internal readonly struct NativeVertexMetadata(StateCache cache, int vao, bool elementBinding,
        GpuResourceMetadataEntry<GpuVertexArrayMetadata>? entry)
    {
        #region Internal API
        /// <summary>Advances helper revision after execution; exceptions retain unavailable metadata.</summary>
        internal void Complete(bool nativeReturned)
        {
            if (elementBinding) cache.elementArrayBufferByVao.Remove(vao);
            if (!nativeReturned || entry is null || !entry.Manager.CanObserveNativeMetadata
                || !entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
            // No native fields are asserted by the empty schema. Even a rejected command
            // conservatively invalidates helper assumptions without consuming its GL error.
            entry.Manager.PublishMetadata(entry, default(GpuVertexArrayMetadata), GpuMetadataChange.Metadata);
        }
        #endregion
    }
}
