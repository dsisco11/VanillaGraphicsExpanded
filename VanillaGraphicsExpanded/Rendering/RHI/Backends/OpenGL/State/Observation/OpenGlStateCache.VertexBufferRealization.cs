namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains buffer-side realization from vertex binding without introducing a canonical VAO layout.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Applies EXT default initialization only for an original generated array name.</summary>
    internal void ObserveExtVertexBufferRealization(int vao, int buffer)
    {
        if (RealizeExtVertexArray(vao)) RealizeNativeBuffer(buffer, true);
    }

    /// <summary>Establishes only the array default state shared by eligible EXT named commands.</summary>
    internal bool RealizeExtVertexArray(int vao)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (vao == 0 || manager?.CanObserveNativeMetadata != true) return false;
        var entry = manager.FindObservedResource(GpuResourceKind.VertexArray, vao) as GpuResourceMetadataEntry<GpuVertexArrayMetadata>;
        if (entry is null)
        {
            // A supported EXT use may refer to an older generated name. Record this
            // specific ambiguity for explicit adoption; never invent a default layout.
            entry = manager.RegisterCreatedResource<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray, vao);
            entry.InitialDiscovery = GpuInitialDiscovery.ImplicitCreation;
            entry.BeginRefresh(GpuMetadataChange.Metadata);
            return false;
        }
        if (entry.InitialDiscovery != GpuInitialDiscovery.None || entry.PendingChange != GpuMetadataChange.None) return false;
        if (!entry.IsRealized)
        {
            manager.PublishMetadata(entry, new GpuVertexArrayMetadata(), GpuMetadataChange.None);
            EstablishVertexArrayElementKnowledge(vao, 0);
        }
        return true;
    }

    /// <summary>Establishes the reserved buffer only when the vertex-binding request is accepted by known facts.</summary>
    internal void ObserveVertexBufferRealization(int vao, bool named, uint index, int buffer, nint offset, int stride)
    {
        if (offset < 0 || stride < 0)
        {
            // Native implementations may initialize a valid generated buffer before
            // rejecting the vertex update. Preserve existing facts, but do not invent
            // defaults for a reserved name on its next accepted bind.
            MarkUncertainBufferRealization(buffer);
            return;
        }
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext)
        {
            MarkUncertainBufferRealization(buffer);
            return;
        }
        if (index >= GpuSupport.Graphics.MaxVertexBindings || stride > GpuSupport.Graphics.MaxVertexStride)
        {
            MarkUncertainBufferRealization(buffer);
            return;
        }
        bool arrayEstablished = named ? vao != 0 && HasEstablishedNativeIdentity(GpuResourceKind.VertexArray, vao)
            : GetCurrentVao() != 0;
        if (arrayEstablished) RealizeNativeBuffer(buffer, false);
        else if (named && GpuResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.VertexArray, vao)
            is not { IsRealized: false }) MarkUncertainBufferRealization(buffer);
    }
    #endregion
}
