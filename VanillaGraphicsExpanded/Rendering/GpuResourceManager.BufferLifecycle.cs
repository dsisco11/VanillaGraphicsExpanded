using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Maintains buffer and external-memory identities from their observed native lifecycles.</summary>
internal sealed partial class GpuResourceManager
{
    private bool resourceObservationActive;

    #region Internal API
    /// <summary>Closes initial-discovery admission before covered native creation is observed.</summary>
    internal void ActivateResourceObservation()
    {
        RequireMetadataContext();
        resourceObservationActive = true;
    }

    /// <summary>Records integration-owned proof of an existing buffer before hook activation.</summary>
    internal GpuResourceMetadataEntry<GpuBufferMetadata> RecordPreexistingBuffer(nint id)
    {
        RequireMetadataContext();
        if (resourceObservationActive)
            throw new InvalidOperationException("Pre-install buffer provenance must be recorded before observation begins.");
        if (FindResource(GpuResourceKind.Buffer, id) is not null)
            throw new InvalidOperationException("The original buffer identity is already registered.");
        var entry = RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, id);
        // The integration handoff proves an existing object, not an unused generated name.
        // Retain that fact even if its one initial descriptor discovery fails.
        entry.Realize();
        entry.InitialDiscoveryPending = true;
        return entry;
    }

    /// <summary>Consumes a factory result once without overwriting a live allocation from stale output bytes.</summary>
    internal void ObserveBufferCreation(nint id, bool realized)
    {
        if (id == 0 || !CanObserveNativeMetadata || FindResource(GpuResourceKind.Buffer, id) is not null) return;
        var entry = RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, id);
        if (realized) PublishMetadata(entry, new(0, RHI.BufferUsage.StaticDraw), GpuMetadataChange.None);
    }

    /// <summary>Establishes native buffer defaults on the same reserved or legal bind-created identity.</summary>
    internal GpuResourceMetadataEntry<GpuBufferMetadata>? RealizeObservedBuffer(nint id, bool mayCreate)
    {
        if (id == 0 || !CanObserveNativeMetadata) return null;
        var entry = FindObservedResource(GpuResourceKind.Buffer, id) as GpuResourceMetadataEntry<GpuBufferMetadata>;
        if (entry is null && mayCreate && FindResource(GpuResourceKind.Buffer, id) is null)
            entry = RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, id);
        if (entry is null) return null;
        if (!entry.IsRealized)
        {
            // A prior limit-dependent command may have realized and configured this name.
            // A later accepted bind proves identity, but cannot restore lost default facts.
            if (entry.InitialDiscoveryPending || (entry.PendingChange & GpuMetadataChange.Storage) != 0) entry.Realize();
            else PublishMetadata(entry, new(0, RHI.BufferUsage.StaticDraw), GpuMetadataChange.None);
        }
        return entry;
    }

    /// <summary>Admits a returned external-memory object with its explicit empty schema and no payload claim.</summary>
    internal void ObserveMemoryObjectCreation(nint id)
    {
        if (id == 0 || !CanObserveNativeMetadata || FindResource(GpuResourceKind.MemoryObject, id) is not null) return;
        var entry = RegisterCreatedResource<GpuMemoryObjectMetadata>(GpuResourceKind.MemoryObject, id);
        PublishMetadata(entry, new GpuMemoryObjectMetadata(), GpuMetadataChange.None);
    }
    #endregion
}
