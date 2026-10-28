namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Reflects external native mutations without applying managed reentry admission as a veto.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Marks original managed candidates interrupted after an external mutation, before they can publish stale results.</summary>
    internal void InterruptNativeResourceOperation(GpuResourceKind kind, nint id)
    {
        var context = ResourceOperationContext;
        for (var item = resourceOperation; item is not null; item = item.Previous)
            if (!item.Deleted && item.Context == context && item.Handle == new GpuResourceHandle(kind, id))
                item.Interrupt();
    }

    /// <summary>Withdraws a conflicting candidate without taking over its flags or releasing its exclusion.</summary>
    internal static bool InterruptNativeMetadataCompletion<T>(GpuResourceMetadataEntry<T> entry,
        GpuMetadataChange change) where T : struct
    {
        if (!entry.OperationInProgress && !entry.CompletionInProgress) return false;
        entry.CompletionInterrupted = true;
        entry.BeginRefresh(change);
        return true;
    }
    #endregion
}
