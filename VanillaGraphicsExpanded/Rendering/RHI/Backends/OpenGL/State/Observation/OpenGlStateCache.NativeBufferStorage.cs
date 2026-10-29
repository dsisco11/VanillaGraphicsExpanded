using System;
using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains storage facts from supplied native allocation requests without descriptor discovery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Retains a possibly successful realization until identity can be established.</summary>
    internal void MarkUncertainBufferRealization(int id)
    {
        if (GpuResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Buffer, id)
            is GpuResourceMetadataEntry<GpuBufferMetadata> { IsRealized: false } entry)
            entry.BeginRefresh(GpuMetadataChange.Metadata);
    }

    /// <summary>Realizes legal names through the original registry and establishes empty-object execution defaults.</summary>
    internal GpuResourceMetadataEntry<GpuBufferMetadata>? RealizeNativeBuffer(int id, bool mayCreate)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        bool wasRealized = manager?.FindObservedResource(GpuResourceKind.Buffer, id)?.IsRealized == true;
        var entry = manager?.RealizeObservedBuffer(id, mayCreate);
        if (!wasRealized && entry is { IsUsable: true }) bufferMappings[id] = false;
        return entry;
    }

    /// <summary>Classifies rejection before disturbing identity, operations or canonical availability.</summary>
    internal static void ObserveBufferStorage(int selector, bool named, bool extCreation, nint size,
        int usage, int flags, bool immutable, bool unresolvedImport = false)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, requireCapabilities: false)) return;
        if (!named && !cache.IsBufferTargetSupported((BufferTarget)selector)) return;
        int id = named ? selector : cache.GetBoundBuffer((BufferTarget)selector);
        if (id == 0) return;
        // EXT DSA initializes the default object even when its subsequent update is rejected.
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var entry = extCreation ? cache.RealizeNativeBuffer(id, true)
            : manager.FindObservedResource(GpuResourceKind.Buffer, id) as GpuResourceMetadataEntry<GpuBufferMetadata>;
        if (size < 0 || immutable && size == 0) return;
        if (!immutable && !Enum.IsDefined((BufferUsageHint)usage)) return;
        const Native.BufferStorageFlags baseFlags = Native.BufferStorageFlags.MapReadBit | Native.BufferStorageFlags.MapWriteBit
            | Native.BufferStorageFlags.MapPersistentBit | Native.BufferStorageFlags.MapCoherentBit
            | Native.BufferStorageFlags.DynamicStorageBit | Native.BufferStorageFlags.ClientStorageBit;
        var nativeFlags = (Native.BufferStorageFlags)flags;
        var sparseFlag = (Native.BufferStorageFlags)Native.ArbSparseBuffer.SparseStorageBitArb;
        var allowed = baseFlags | sparseFlag;
        bool sparse = (nativeFlags & sparseFlag) != 0;
        bool capabilitiesKnown = GpuSupport.HasCachedCapabilitiesForCurrentContext;
        if (sparse && capabilitiesKnown && !GpuSupport.Supports("GL_ARB_sparse_buffer")) return;
        if (immutable && ((nativeFlags & ~allowed) != 0
            || (nativeFlags & Native.BufferStorageFlags.MapPersistentBit) != 0
                && (nativeFlags & (Native.BufferStorageFlags.MapReadBit | Native.BufferStorageFlags.MapWriteBit)) == 0
            || (nativeFlags & Native.BufferStorageFlags.MapCoherentBit) != 0
                && (nativeFlags & Native.BufferStorageFlags.MapPersistentBit) == 0)) return;
        if (entry is { MetadataRevision: not 0 } && entry.Metadata.ImmutableStorage) return;
        if ((nativeFlags & (Native.BufferStorageFlags)Native.ArbSparseBuffer.SparseStorageBitArb) != 0
            && (nativeFlags & (Native.BufferStorageFlags.MapReadBit | Native.BufferStorageFlags.MapWriteBit)) != 0) return;
        if (entry is { IsRealized: false })
        {
            // A possibly realized name may have accepted this allocation. Record that
            // descriptor uncertainty so a later bind cannot fabricate empty defaults.
            // A proven untouched reservation instead retains its rejected-command facts.
            if (entry.PendingChange != GpuMetadataChange.None) entry.BeginRefresh(GpuMetadataChange.Storage);
            return;
        }
        cache.InterruptNativeResourceOperation(GpuResourceKind.Buffer, id);
        // A local supplied-snapshot owner may have an exclusion without a global entry.
        // Interrupt it after relevance/rejection classification without inventing registration.
        if (entry is null) return;
        if (InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage)) return;
        // A withdrawn prior snapshot cannot prove mutability. Imported allocation acceptance
        // also depends on payload facts that the empty memory schema does not certify.
        if (!entry.IsUsable || unresolvedImport || sparse && !capabilitiesKnown)
        {
            entry.BeginRefresh(GpuMetadataChange.Storage);
            return;
        }
        var candidate = new GpuBufferMetadata(size, immutable ? BufferUsage.DynamicDraw
            : OpenGLBufferValues.Usage((BufferUsageHint)usage), immutable,
            immutable ? OpenGLBufferValues.Storage(nativeFlags) : 0);
        cache.bufferMappings[id] = false;
        manager.PublishMetadata(entry, candidate, GpuMetadataChange.Storage);
    }

    /// <summary>Uses cached optional-target support without issuing capability reads from a callback.</summary>
    internal bool IsBufferTargetSupported(BufferTarget target)
    {
        if (!Enum.IsDefined(target)) return false;
        return target != BufferTarget.ParameterBuffer || GpuSupport.HasCachedCapabilitiesForCurrentContext
            && (GpuSupport.Graphics.ApiVersion is { Major: > 4 } or { Major: 4, Minor: >= 6 }
                || GpuSupport.Supports("GL_ARB_indirect_parameters"));
    }
    #endregion
}
