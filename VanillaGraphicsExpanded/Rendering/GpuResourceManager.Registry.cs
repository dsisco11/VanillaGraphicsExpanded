using System;
using System.Collections.Concurrent;
using System.Threading;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns native metadata registrations within one originating context lifetime.</summary>
internal sealed partial class GpuResourceManager
{
    private readonly ConcurrentDictionary<GpuResourceHandle, GpuResourceRegistration> metadataRegistry = new();
    private readonly Func<(nint Handle, long Generation)> currentMetadataContext;
    private readonly Func<(nint Handle, long Generation), bool> metadataContextAlive;
    private int metadataThreadId;
    private int metadataRegistryRetired;

    /// <summary>Rejects shared usability immediately when manager shutdown or context retirement begins.</summary>
    internal bool IsMetadataRegistryRetired => IsDisposed || Volatile.Read(ref metadataRegistryRetired) != 0;

    #region Internal API
    #region Construction and registration
    /// <summary>Captures the current context and subscribes once to its terminal retirement.</summary>
    internal GpuResourceManager() : this(RenderContextRegistry.Current, RenderContextRegistry.IsAlive) { }

    /// <summary>Supplies context boundaries for deterministic registry tests without native GL calls.</summary>
    internal GpuResourceManager(Func<(nint Handle, long Generation)> currentContext,
        Func<(nint Handle, long Generation), bool> isContextAlive)
    {
        currentMetadataContext = currentContext;
        metadataContextAlive = isContextAlive;
        context = currentContext();
        RenderContextRegistry.Retiring += OnMetadataContextRetiring;
    }

    /// <summary>Registers a generated name without claiming initialized storage or native ownership.</summary>
    internal GpuResourceMetadataEntry<TMetadata> RegisterCreatedResource<TMetadata>(GpuResourceKind kind, nint id,
        IGpuResourceMetadataReader<TMetadata>? reader = null) where TMetadata : struct
    {
        RequireMetadataContext();
        ValidateMetadataType<TMetadata>(kind, id);
        var key = new GpuResourceHandle(kind, id);
        var entry = (GpuResourceMetadataEntry<TMetadata>)metadataRegistry.GetOrAdd(key,
            static (handle, args) => new GpuResourceMetadataEntry<TMetadata>(args.Manager, handle.Kind, handle.Id, args.Reader),
            (Manager: this, Reader: reader));
        CompleteRegistryAdmission(entry);
        RequireLiveEntry(entry);
        entry.PruneAssociations();
        entry.Reader ??= reader;
        return entry;
    }

    /// <summary>Reuses tracked facts or reads an external object, optionally refreshing an existing registration.</summary>
    internal GpuResourceMetadataEntry<TMetadata>? AcquireResource<TMetadata>(GpuResourceKind kind, nint id,
        IGpuResourceMetadataReader<TMetadata> reader, bool refreshExisting = false) where TMetadata : struct
    {
        ArgumentNullException.ThrowIfNull(reader);
        RequireMetadataContext();
        ValidateMetadataType<TMetadata>(kind, id);
        var key = new GpuResourceHandle(kind, id);
        if (metadataRegistry.TryGetValue(key, out var existing))
        {
            existing.PruneAssociations();
            var tracked = (GpuResourceMetadataEntry<TMetadata>)existing;
            if (tracked.State != GpuResourceRegistrationState.Live) return null;
            tracked.Reader ??= reader;
            if (tracked.MetadataAvailable && !refreshExisting) return tracked;
            tracked.Reader = reader;
            return RefreshMetadata(tracked, tracked.MetadataRevision == 0 ? GpuMetadataChange.None : GpuMetadataChange.Metadata) ? tracked : null;
        }
        if (!reader.TryRead(id, out var candidate)) return null;
        RequireMetadataContext();
        var entry = new GpuResourceMetadataEntry<TMetadata>(this, kind, id, reader);
        // Native acquisition is context-thread confined. Keep the map operation atomic and
        // never run native readers in a GetOrAdd factory, whose execution can be repeated.
        if (!metadataRegistry.TryAdd(key, entry))
            throw new InvalidOperationException("The native resource was registered during metadata acquisition.");
        CompleteRegistryAdmission(entry);
        RequireLiveEntry(entry);
        entry.Publish(candidate, GpuMetadataChange.None);
        return entry;
    }

    /// <summary>Looks up registration identity on any thread without native queries or ownership transfer.</summary>
    /// <remarks>Native metadata reads and updates still belong to the originating context thread.</remarks>
    internal GpuResourceRegistration? FindResource(GpuResourceKind kind, nint id)
    {
        RequireActiveRegistry();
        if (!metadataRegistry.TryGetValue(new(kind, id), out var entry)) return null;
        return IsRegistered(entry) ? entry : null;
    }
    #endregion

    #region Wrapper associations
    /// <summary>Initializes an ordinary typed wrapper from the shared canonical entry.</summary>
    internal void AssociateResource<TMetadata>(GpuResourceMetadataEntry<TMetadata> entry, GpuResource<TMetadata> resource)
        where TMetadata : struct
    {
        ArgumentNullException.ThrowIfNull(resource);
        RequireMetadataContext();
        RequireLiveEntry(entry);
        ValidateAssociation(entry, resource);
        entry.Associate(resource);
    }

    /// <summary>Explicitly removes a non-owning association without retiring its canonical native object.</summary>
    internal void UnregisterResource(GpuResourceRegistration entry, GpuResource resource)
    {
        if (!ReferenceEquals(entry.Manager, this)) throw new InvalidOperationException("The registration belongs to another manager.");
        entry.Unregister(resource);
    }

    #endregion

    #region Retirement
    /// <summary>Marks scheduled owner retirement unavailable atomically; native scheduling remains with the owner.</summary>
    internal bool RetireResource(GpuResourceRegistration entry)
    {
        if (!ReferenceEquals(entry.Manager, this)) throw new InvalidOperationException("The registration belongs to another manager.");
        entry.RequireRetirement();
        return entry.TryRetire();
    }

    /// <summary>Removes a deleted name and invalidates its aliases before that name can be registered again.</summary>
    internal void ObserveDeletion(GpuResourceKind kind, nint id)
    {
        if (id == 0 || context != currentMetadataContext()) return;
        if (metadataRegistry.TryRemove(new(kind, id), out var entry))
        {
            entry.Invalidate(GpuResourceRegistrationState.Deleted);
        }
        // A tracked dependent can refer to an externally owned, unregistered backing name.
        InvalidateDeletionDependencies(kind, id);
    }

    /// <summary>Checks captured registration identity for deferred work without reacquiring a reused numeric name.</summary>
    internal bool IsRegistered(GpuResourceRegistration entry)
    {
        return !IsMetadataRegistryRetired && ReferenceEquals(entry.Manager, this)
            && metadataRegistry.TryGetValue(entry.Handle, out var current) && ReferenceEquals(current, entry);
    }
    #endregion
    #endregion

    #region Private
    #region Context and registration validation
    /// <summary>Ensures all ordinary registry operations and native reads stay on their originating context thread.</summary>
    private void RequireMetadataContext()
    {
        RequireActiveRegistry();
        var current = currentMetadataContext();
        if (current == default || !metadataContextAlive(current))
            throw new InvalidOperationException("Canonical metadata requires a live registered native context.");
        if (context == default) context = current;
        if (current != context) throw new InvalidOperationException("Canonical metadata belongs to another context lifetime.");
        int thread = Environment.CurrentManagedThreadId;
        Interlocked.CompareExchange(ref metadataThreadId, thread, 0);
        if (Volatile.Read(ref metadataThreadId) != thread)
            throw new InvalidOperationException("Canonical metadata must be accessed on its originating context thread.");
    }

    /// <summary>Rejects new registrations after terminal shutdown or context retirement.</summary>
    private void RequireActiveRegistry()
    {
        if (IsDisposed || Volatile.Read(ref metadataRegistryRetired) != 0) throw new ObjectDisposedException(nameof(GpuResourceManager));
    }

    /// <summary>Ensures an update cannot revive a removed entry or mutate another manager's registry.</summary>
    private void RequireLiveEntry(GpuResourceRegistration entry)
    {
        RequireActiveRegistry();
        if (!IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live)
            throw new InvalidOperationException("The original resource registration is no longer live.");
    }

    /// <summary>Closes the race between concurrent map admission and terminal context retirement.</summary>
    private void CompleteRegistryAdmission(GpuResourceRegistration entry)
    {
        if (!IsMetadataRegistryRetired) return;
        // Context retirement can finish its sweep before a context-thread insertion completes.
        // Remove only this exact entry so cleanup cannot remove a replacement registration.
        metadataRegistry.TryRemove(new System.Collections.Generic.KeyValuePair<GpuResourceHandle, GpuResourceRegistration>(entry.Handle, entry));
        entry.Invalidate(GpuResourceRegistrationState.ContextRetired);
        RequireActiveRegistry();
    }

    /// <summary>Checks local identity and disposal without changing wrapper ownership.</summary>
    private static void ValidateAssociation(GpuResourceRegistration entry, GpuResource resource)
    {
        ObjectDisposedException.ThrowIf(resource.IsDisposed, resource);
        if (resource.ResourceId != entry.Id || resource.Kind != entry.Kind)
            throw new ArgumentException("The wrapper does not represent this native object.", nameof(resource));
    }

    /// <summary>Prevents cross-kind metadata confusion and excludes the default framebuffer from native registration.</summary>
    private static void ValidateMetadataType<TMetadata>(GpuResourceKind kind, nint id) where TMetadata : struct
    {
        if (id == 0) throw new ArgumentOutOfRangeException(nameof(id));
        Type expected = kind switch
        {
            GpuResourceKind.Texture => typeof(GpuTextureMetadata), GpuResourceKind.Buffer => typeof(GpuBufferMetadata),
            GpuResourceKind.Renderbuffer => typeof(GpuRenderbufferMetadata), GpuResourceKind.Framebuffer => typeof(GpuFramebufferMetadata),
            GpuResourceKind.VertexArray => typeof(GpuVertexArrayMetadata), GpuResourceKind.Program => typeof(GpuProgramMetadata),
            GpuResourceKind.Shader => typeof(GpuShaderMetadata), GpuResourceKind.Query => typeof(GpuQueryMetadata),
            GpuResourceKind.Sync => typeof(GpuSyncMetadata), GpuResourceKind.TransformFeedback => typeof(GpuTransformFeedbackMetadata),
            GpuResourceKind.Sampler => typeof(GpuSamplerMetadata), GpuResourceKind.ProgramPipeline => typeof(GpuProgramPipelineMetadata),
            GpuResourceKind.Semaphore => typeof(GpuSemaphoreMetadata), GpuResourceKind.MemoryObject => typeof(GpuMemoryObjectMetadata),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (expected != typeof(TMetadata)) throw new ArgumentException("Metadata type does not match the resource kind.", nameof(kind));
    }

    #endregion

    #region Context retirement
    /// <summary>Retires only this manager when the context registry announces its originating lifetime.</summary>
    private void OnMetadataContextRetiring((nint Handle, long Generation) retiring)
    {
        if (context != default && context == retiring) RetireMetadataRegistry();
    }

    /// <summary>Releases registry retention without issuing GL calls into a retiring or replacement namespace.</summary>
    private void RetireMetadataRegistry()
    {
        if (Interlocked.Exchange(ref metadataRegistryRetired, 1) != 0) return;
        RenderContextRegistry.Retiring -= OnMetadataContextRetiring;
        foreach (var pair in metadataRegistry)
            if (metadataRegistry.TryRemove(pair.Key, out var entry))
                entry.Invalidate(GpuResourceRegistrationState.ContextRetired);
    }
    #endregion
    #endregion
}
