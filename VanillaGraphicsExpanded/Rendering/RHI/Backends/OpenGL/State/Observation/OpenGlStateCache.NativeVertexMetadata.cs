namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes vertex-array configuration revisions without introducing a native layout mirror.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Captures an existing VAO registration and withdraws helper assumptions before mutation.</summary>
    internal static NativeVertexMetadata BeginNativeVertexMetadata(int selector, bool named, bool elementBinding)
    {
        var cache = Current;
        int vao = named ? selector : cache.GetCurrentVao();
        cache.RequireResourceOperationAvailable(GpuResourceKind.VertexArray, vao);
        cache.InvalidateVertexConfiguration(vao);
        if (elementBinding) cache.elementArrayBufferByVao.Remove(vao);
        var manager = GpuResourceManagerSystem.CurrentManager;
        var entry = manager?.FindObservedResource(GpuResourceKind.VertexArray, vao)
            as GpuResourceMetadataEntry<GpuVertexArrayMetadata>;
        if (entry is not null)
        {
            if (entry.OperationInProgress || entry.CompletionInProgress) throw new System.InvalidOperationException("A resource metadata operation is already in progress.");
            entry.OperationInProgress = true;
            entry.BeginRefresh(GpuMetadataChange.Metadata);
        }
        return new(cache, vao, elementBinding, entry, Integration.RenderContextRegistry.CaptureResourceContext());
    }
    #endregion

    /// <summary>Retains the original VAO entry and invalidates only affected binding knowledge.</summary>
    internal readonly struct NativeVertexMetadata(OpenGlStateCache cache, int vao, bool elementBinding,
        GpuResourceMetadataEntry<GpuVertexArrayMetadata>? entry, (nint Handle, long Generation) context)
    {
        #region Internal API
        /// <summary>Advances helper revision after execution; exceptions retain unavailable metadata.</summary>
        internal void Complete(bool nativeReturned)
        {
            if (entry is not null) entry.OperationInProgress = false;
            if (!cache.OwnsKnowledgeFor(context) || !Integration.RenderContextRegistry.IsCurrentResourceContext(context)) return;
            cache.InvalidateVertexConfiguration(vao);
            if (elementBinding) cache.elementArrayBufferByVao.Remove(vao);
            if (!nativeReturned || entry is null || !entry.Manager.CanObserveNativeMetadata
                || !entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
            // No native fields are asserted by the empty schema. Even a rejected command
            // conservatively invalidates helper assumptions without consuming its GL error.
            entry.Manager.PublishMetadata(entry, default(GpuVertexArrayMetadata), GpuMetadataChange.Metadata);
        }
        /// <summary>Retains a controlled EBO result only for its original context and live association.</summary>
        internal void EstablishElementBinding(int buffer)
        {
            if (!cache.OwnsKnowledgeFor(context) || !Integration.RenderContextRegistry.IsCurrentResourceContext(context)) return;
            if (entry is not null && (!entry.Manager.IsRegistered(entry)
                || entry.State != GpuResourceRegistrationState.Live)) return;
            cache.elementArrayBufferByVao[vao] = buffer;
        }
        #endregion
    }
}
