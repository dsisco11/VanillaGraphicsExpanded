using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires explicitly external buffer facts for engine geometry adapters.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Reuses covered canonical facts before requesting unknown external storage from the backend.</summary>
    internal static GpuBufferMetadata AcquireBufferMetadata(int id)
    {
        if (id == 0) throw new ArgumentException("A native buffer name is required.", nameof(id));
        if (GpuResourceManagerSystem.CurrentManager is { CanObserveNativeMetadata: true } manager)
            return manager.AcquireResource(GpuResourceKind.Buffer, id, new GpuBufferMetadataReader())?.Metadata
                ?? throw new InvalidOperationException("External buffer storage is unavailable.");
        if (!RHI.RHICommandContext.Current.TryReadBufferMetadata(id, out var data))
            throw new InvalidOperationException("Context-local external buffer storage is unavailable.");
        return data;
    }
    #endregion
}
