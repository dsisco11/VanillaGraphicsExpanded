using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns transient vertex binding recovery for registered resources and context-local tools.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Acquires requested unknown fields only from the helper's original live object and context.</summary>
    internal static void AcquireVertexBindingFacts(GpuVao owner, int vao, int index,
        (nint Handle, long Generation) context, GpuVertexBindingFields fields,
        ref int buffer, ref nint offset, ref int stride, ref int divisor)
    {
        ObjectDisposedException.ThrowIf(!owner.IsValid || owner.VertexArrayId != vao, owner);
        RequireWrapperMetadataAccess(owner);
        if (context != Integration.RenderContextRegistry.Current())
            throw new InvalidOperationException("The vertex binding belongs to another native context.");
        var manager = owner.ResourceManager;
        var entry = manager?.GetResourceRegistration(owner);
        if (entry is not null && (!manager!.CanObserveNativeMetadata || !manager.IsResourceMetadataAvailable(owner)))
            throw new InvalidOperationException("Vertex array metadata is unavailable in this context.");
        if (fields == GpuVertexBindingFields.None) return;
        if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            throw new InvalidOperationException("Unknown binding-index facts cannot be recovered on this capability path.");
        // Read into temporaries so a failed multi-field read cannot certify a partial helper.
        int nextBuffer = buffer, nextStride = stride, nextDivisor = divisor;
        nint nextOffset = offset;
        GpuVertexBindingReader.Read(vao, index, fields, ref nextBuffer, ref nextOffset, ref nextStride, ref nextDivisor);
        if (context != Integration.RenderContextRegistry.Current() || !owner.IsValid
            || entry is not null && (!manager!.IsRegistered(entry) || !manager.IsResourceMetadataAvailable(owner)))
            throw new InvalidOperationException("The original vertex array retired during acquisition.");
        buffer = nextBuffer; offset = nextOffset; stride = nextStride; divisor = nextDivisor;
    }
    #endregion
}
