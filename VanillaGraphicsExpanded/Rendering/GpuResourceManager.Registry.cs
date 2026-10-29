using VanillaGraphicsExpanded.Rendering.RHI;
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

    /// <summary>Reuses available canonical facts; acquires or recovers only missing facts.</summary>
    internal GpuResourceMetadataEntry<TMetadata>? AcquireResource<TMetadata>(GpuResourceKind kind, nint id,
        IGpuResourceMetadataReader<TMetadata> reader) where TMetadata : struct
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
            // A completed candidate is available before dependent publication. An open
            // operation with an unpublished candidate remains unavailable below.
            if (tracked.IsUsable) return tracked;
            if (tracked.OperationInProgress) return null;
            if (tracked.CompletionInProgress) return null;
            // TODO(Rendering.MetadataLifecycle.todo, resource acquisition tasks): apply the
            // recorded-discovery gate to the remaining kinds as each family is migrated.
            if (UsesMaintainedLifecycle(kind))
            {
                var discovery = tracked.InitialDiscovery;
                if (discovery == GpuInitialDiscovery.None) return null;
                tracked.InitialDiscovery = GpuInitialDiscovery.None;
                // A rejected EXT call may have supplied the wrong target for an older
                // texture. Initial discovery must identify its actual target independently.
                if (discovery == GpuInitialDiscovery.ImplicitCreation && reader is GpuTextureMetadataReader)
                    reader = (IGpuResourceMetadataReader<TMetadata>)(object)GpuTextureMetadataReader.Instance;
            }
            tracked.Reader = reader;
            return RefreshMetadata(tracked, tracked.MetadataRevision == 0 ? GpuMetadataChange.None : GpuMetadataChange.Metadata) ? tracked : null;
        }
        if (UsesMaintainedLifecycle(kind)) return null;
        // TODO(Rendering.MetadataLifecycle.todo, remaining family acquisition tasks): remove
        // provisional readback admission once each kind maintains its creation/configuration facts.
        // Admit an unavailable provisional identity before reading so reentrant acquisition
        // cannot recursively rediscover the same object. Failed reads remove only this entry.
        var entry = RegisterCreatedResource(kind, id, reader);
        bool acquired = false;
        try
        {
            acquired = RefreshMetadata(entry, GpuMetadataChange.None);
            return acquired ? entry : null;
        }
        finally
        {
            if (!acquired)
            {
                metadataRegistry.TryRemove(new System.Collections.Generic.KeyValuePair<GpuResourceHandle, GpuResourceRegistration>(key, entry));
                entry.Invalidate(GpuResourceRegistrationState.Deleted);
            }
        }
    }

    /// <summary>Explicitly recovers external facts even when a previous canonical snapshot is available.</summary>
    internal GpuResourceMetadataEntry<TMetadata>? RecoverResource<TMetadata>(GpuResourceKind kind, nint id,
        IGpuResourceMetadataReader<TMetadata> reader) where TMetadata : struct
    {
        RequireMetadataContext();
        ValidateMetadataType<TMetadata>(kind, id);
        var entry = FindResource(kind, id) as GpuResourceMetadataEntry<TMetadata>;
        // TODO(Rendering.MetadataLifecycle.todo, remaining family recovery tasks): require
        // original registrations for every kind; remove discovery through this recovery fallback.
        if (entry is null) return UsesMaintainedLifecycle(kind)
            ? null : AcquireResource(kind, id, reader);
        if (entry.State != GpuResourceRegistrationState.Live || entry.OperationInProgress || entry.CompletionInProgress) return null;
        if (kind == GpuResourceKind.Buffer && !entry.IsRealized && entry.InitialDiscovery == GpuInitialDiscovery.None) return null;
        entry.InitialDiscovery = GpuInitialDiscovery.None;
        if (entry.MetadataRevision == 0 && reader is GpuTextureMetadataReader)
            reader = (IGpuResourceMetadataReader<TMetadata>)(object)GpuTextureMetadataReader.Instance;
        if (entry is GpuResourceMetadataEntry<GpuMemoryObjectMetadata> memory)
        {
            if (!memory.IsRealized) return null;
            PublishMetadata(memory, new GpuMemoryObjectMetadata(), GpuMetadataChange.Metadata);
            return entry;
        }
        // Empty schemas recover their established original lifetime without a descriptor reader.
        // A reservation cannot use recovery to manufacture realization or import success.
        if (entry is GpuResourceMetadataEntry<GpuQueryMetadata> query)
        {
            if (!query.IsRealized) return null;
            PublishMetadata(query, new GpuQueryMetadata(), GpuMetadataChange.Metadata);
            return entry;
        }
        if (entry is GpuResourceMetadataEntry<GpuSyncMetadata> sync)
        {
            if (!sync.IsRealized) return null;
            PublishMetadata(sync, new GpuSyncMetadata(), GpuMetadataChange.Metadata);
            return entry;
        }
        if (entry is GpuResourceMetadataEntry<GpuSemaphoreMetadata> semaphore)
        {
            if (!semaphore.IsRealized) return null;
            PublishMetadata(semaphore, new GpuSemaphoreMetadata(), GpuMetadataChange.Metadata);
            return entry;
        }
        entry.Reader = reader;
        return RefreshMetadata(entry, GpuMetadataChange.Metadata) ? entry : null;
    }

    /// <summary>Returns an available canonical entry without issuing native reads or initiating recovery.</summary>
    internal GpuResourceMetadataEntry<TMetadata>? LookupAvailableResource<TMetadata>(GpuResourceKind kind, nint id)
        where TMetadata : struct
    {
        RequireMetadataContext();
        ValidateMetadataType<TMetadata>(kind, id);
        return FindResource(kind, id) is GpuResourceMetadataEntry<TMetadata> { IsUsable: true } entry ? entry : null;
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
    internal void ObserveDeletion(GpuResourceKind kind, nint id, int? readFramebuffer = null, int? drawFramebuffer = null,
        bool external = false)
    {
        if (id == 0 || context != currentMetadataContext()) return;
        RHICommandContext.CancelResourceOperation(kind, id);
        GpuResourceRegistration? deleted = null;
        if (metadataRegistry.TryRemove(new(kind, id), out var entry))
        {
            deleted = entry;
            if (external && entry is GpuResourceMetadataEntry<GpuTextureMetadata> texture)
                texture.ClearImageRecipientSnapshots();
            else if (external && entry is GpuResourceMetadataEntry<GpuRenderbufferMetadata> renderbuffer)
                renderbuffer.ClearImageRecipientSnapshots();
            entry.Invalidate(GpuResourceRegistrationState.Deleted);
            if (kind == GpuResourceKind.Texture) WithdrawChangedTextureViews(entry);
        }
        // A tracked dependent can refer to an externally owned, unregistered backing name.
        InvalidateDeletionDependencies(kind, id, deleted, readFramebuffer, drawFramebuffer, external);
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
    /// <summary>Identifies families whose covered creation facts cannot be replaced by implicit discovery.</summary>
    private static bool UsesMaintainedLifecycle(GpuResourceKind kind) => kind is GpuResourceKind.Buffer
        or GpuResourceKind.MemoryObject or GpuResourceKind.Query or GpuResourceKind.Sync or GpuResourceKind.Semaphore
        or GpuResourceKind.Texture or GpuResourceKind.Renderbuffer or GpuResourceKind.Framebuffer;

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
