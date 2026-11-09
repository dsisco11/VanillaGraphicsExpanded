using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies canonical metadata identity, publication and retention without requiring native commands.</summary>
[Collection("CPU state")] // Resource construction captures the process-wide lifecycle service.
public sealed class GpuResourceMetadataRegistryTests
{
    #region Public API
    #region Acquisition and refresh
    /// <summary>Each actual snapshot acquisition validates identity exactly once inside its reader.</summary>
    [Fact]
    public void AcquisitionAndRefreshValidateIdentityOncePerSnapshot()
    {
        // Arrange: Discover one existing buffer through a reader that counts identity checks.
        using var manager = CreateManager();
        var reader = new BufferReader();
        var entry = DiscoverPreexistingBuffer(manager, 7, reader)!;
        // Act / Assert: Initial lookup validates once and reusing the same name adds no read.
        Assert.Equal(1, reader.ValidityChecks);
        Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
        Assert.Equal(1, reader.ValidityChecks);
        // Act / Assert: Each metadata refresh validates the reader once and withdraws unavailable facts.
        Assert.True(manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
        Assert.Equal(2, reader.ValidityChecks);
        reader.Exists = false;
        Assert.False(manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
        Assert.Equal(3, reader.ValidityChecks);
        Assert.False(entry.MetadataAvailable);
        Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, 8, reader));
        Assert.Equal(3, reader.ValidityChecks);
        Assert.Null(manager.FindResource(GpuResourceKind.Buffer, 8));
    }

    /// <summary>External acquisition captures once and each live alias receives the same canonical facts.</summary>
    [Fact]
    public void AcquisitionSharesEntryAndRefreshesAllAliasesOnce()
    {
        // Arrange: Associate two wrappers with one discovered buffer registration.
        using var manager = CreateManager();
        var reader = new BufferReader { Candidate = new(24, BufferUsage.DynamicDraw) };
        var entry = DiscoverPreexistingBuffer(manager, 7, reader)!;
        Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
        using var first = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var second = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, first);
        manager.AssociateResource(entry, second);
        Assert.Equal(reader.Candidate, first.Snapshot);
        Assert.Equal(first.Snapshot, second.Snapshot);
        Assert.True(manager.IsResourceMetadataAvailable(first));
        Assert.Equal(1, reader.Reads);
        // Act / Assert: Replace storage facts and observe one fresh snapshot on both aliases.
        reader.Candidate = new(96, BufferUsage.StaticDraw);
        reader.OnRead = () => { Assert.False(manager.IsResourceMetadataAvailable(first)); Assert.False(manager.IsResourceMetadataAvailable(second)); };
        Assert.True(manager.RefreshMetadata(entry, GpuMetadataChange.Storage));
        Assert.Equal(2, reader.Reads);
        Assert.Equal(reader.Candidate, second.Snapshot);
        Assert.Equal(second.Snapshot, first.Snapshot);
        Assert.True(manager.IsResourceMetadataAvailable(second));
        Assert.Equal(2ul, entry.MetadataRevision);
        Assert.Equal(1ul, entry.StorageRevision);
        Assert.Same(entry, manager.GetResourceRegistration(first));
        Assert.Same(entry, manager.GetResourceRegistration(second));
    }

    /// <summary>Reserved names stay unavailable until observed realization supplies native defaults.</summary>
    [Fact]
    public void CreatedNamesDoNotInventStorageAndCanLaterBeAcquired()
    {
        // Arrange: Reserve a native name and associate a wrapper before observed storage exists.
        using var manager = CreateManager();
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var resource = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, resource);
        // Act / Assert: A missing native object cannot fabricate metadata or trigger reader work.
        Assert.False(manager.IsResourceMetadataAvailable(resource));
        var reader = new BufferReader { Exists = false };
        Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
        Assert.Equal(0, reader.Reads);
        Assert.Equal(0, reader.ValidityChecks);
        // Act: Realize the observed buffer, then acquire its canonical entry.
        manager.RealizeObservedBuffer(7, false);
        // Assert: The original registration becomes available with the newly observed facts.
        Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
        Assert.True(manager.IsResourceMetadataAvailable(resource));
    }

    /// <summary>Failed or throwing readers disable all aliases and later complete refreshes recover them.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshFailureCannotExposeStaleSnapshots(bool throws)
    {
        // Arrange: Associate a live wrapper with one readable buffer entry.
        using var manager = CreateManager();
        var reader = new BufferReader();
        var entry = DiscoverPreexistingBuffer(manager, 7, reader)!;
        using var resource = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, resource);
        // Act: Make the next metadata refresh unavailable or throwing.
        reader.Readable = false; reader.Throws = throws;
        // Assert: Failure withdraws the old snapshot and advances the failed revision.
        if (throws) Assert.Throws<InvalidOperationException>(() => manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
        else Assert.False(manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
        Assert.False(entry.MetadataAvailable);
        Assert.False(manager.IsResourceMetadataAvailable(resource));
        Assert.Equal(2ul, entry.MetadataRevision);
        // Act / Assert: A readable refresh restores the same alias with fresh facts.
        reader.Readable = true; reader.Throws = false;
        Assert.True(manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
        Assert.True(manager.IsResourceMetadataAvailable(resource));
    }

    #endregion

    #region Registration identity and retention
    /// <summary>Observed deletion removes identity so old wrappers cannot attach to a reused native name.</summary>
    [Fact]
    public void DeletionAndReuseNeverReviveOldRegistration()
    {
        // Arrange: Discover a buffer and attach a wrapper to its original registration.
        using var manager = CreateManager();
        var reader = new BufferReader();
        var old = DiscoverPreexistingBuffer(manager, 7, reader)!;
        using var alias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(old, alias);
        // Act: Delete the native name and rediscover the replacement.
        manager.ObserveDeletion(GpuResourceKind.Buffer, 7);
        var replacement = DiscoverPreexistingBuffer(manager, 7, reader)!;
        // Assert: The replacement has a new identity and the old alias remains retired.
        Assert.NotSame(old, replacement);
        Assert.False(manager.IsRegistered(old));
        Assert.True(manager.IsRegistered(replacement));
        Assert.Equal(GpuResourceRegistrationState.Deleted, old.State);
        Assert.False(manager.IsResourceMetadataAvailable(alias));
        Assert.True(manager.IsResourceMetadataRetired(alias));
        Assert.Throws<InvalidOperationException>(() => manager.PublishMetadata(old, reader.Candidate, GpuMetadataChange.Storage));
        Assert.Throws<InvalidOperationException>(() => manager.AssociateResource(replacement, alias));
        Assert.False(manager.IsResourceMetadataAvailable(alias));
    }

    /// <summary>Registry keys retain native width and separate numeric names across kinds and managers.</summary>
    [Fact]
    public void KindContextAndNativeWidthRemainDistinct()
    {
        // Arrange: Register one native name across resource kinds and a second context.
        using var first = CreateManager();
        using var second = new RHIResourceManager(static () => (2, 1), static _ => true);
        nint id = nint.Size == 8 ? unchecked((nint)0x123456789L) : 12345;
        var sync = first.RegisterCreatedResource<GpuSyncMetadata>(GpuResourceKind.Sync, id);
        var buffer = first.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, id);
        var other = second.RegisterCreatedResource<GpuSyncMetadata>(GpuResourceKind.Sync, id);
        // Act / Assert: Names remain distinct by kind and context while invalid ownership is rejected.
        Assert.Equal(id, sync.Id);
        Assert.NotSame(sync, buffer);
        Assert.NotSame(sync, other);
        Assert.Same(sync, first.FindResource(GpuResourceKind.Sync, id));
        Assert.Throws<ArgumentException>(() => first.RegisterCreatedResource<GpuSyncMetadata>(GpuResourceKind.Texture, id));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.RegisterCreatedResource<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer, 0));
        using var alias = new Resource<GpuSyncMetadata>(GpuResourceKind.Sync, id);
        Assert.Throws<InvalidOperationException>(() => second.AssociateResource(sync, alias));
    }

    /// <summary>Collected aliases are pruned while a live alias keeps the canonical entry and metadata.</summary>
    [Fact]
    public void WeakAssociationsArePrunedWithoutDiscardingMetadata()
    {
        // Arrange: keep one recipient alive while a bounded batch leaves scope inside the helper.
        using var manager = CreateManager();
        var entry = DiscoverPreexistingBuffer(manager, 7, new BufferReader())!;
        using var survivor = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, survivor);
        var expectedMetadata = entry.Metadata;
        var expectedRevision = entry.MetadataRevision;
        var abandoned = AssociateTemporaryBatch(manager, entry);

        // Act: request collection until the abandoned wrappers are no longer reachable. These wrappers have no finalizers.
        for (int attempt = 0; attempt < 5 && abandoned.Any(value => value.IsAlive); attempt++)
            GC.Collect();

        // Assert: Pruning dead weak associations leaves the live registration and canonical facts intact.
        Assert.All(abandoned, value => Assert.False(value.IsAlive));
        Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, 7, new BufferReader()));
        Assert.Equal(1, entry.AssociationCount);
        Assert.True(manager.IsResourceMetadataTracked(survivor));
        Assert.True(manager.IsResourceMetadataAvailable(survivor));
        Assert.Equal(expectedMetadata, entry.Metadata);
        Assert.Equal(expectedMetadata, survivor.Snapshot);
        Assert.Equal(expectedRevision, entry.MetadataRevision);
        Assert.Same(entry, manager.GetResourceRegistration(survivor));

        // Act: Unregister the surviving wrapper after verifying that it kept the entry alive.
        manager.UnregisterResource(entry, survivor);
        // Assert: The wrapper is detached while the canonical registration remains usable.
        Assert.False(manager.IsResourceMetadataTracked(survivor));
        Assert.False(manager.IsResourceMetadataAvailable(survivor));
        Assert.True(entry.IsUsable);
        Assert.Same(entry, manager.FindResource(GpuResourceKind.Buffer, 7));
    }

    #endregion

    #region Guards and context boundaries
    /// <summary>Owner retirement keeps original registration identity and withdraws usability exactly once.</summary>
    [Fact]
    public void OwnerRetirementDoesNotCreateReplacementIdentity()
    {
        // Arrange: Reserve one buffer registration without a native allocation.
        using var manager = CreateManager();
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        // Act / Assert: Retiring is terminal for usability but preserves the original registration until deletion.
        Assert.True(manager.RetireResource(entry));
        Assert.False(manager.RetireResource(entry));
        Assert.False(entry.IsUsable);
        Assert.True(manager.IsRegistered(entry));
        Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, 7, new BufferReader()));
    }

    /// <summary>Native mutations remain context-thread confined while registration identity can be looked up concurrently.</summary>
    [Fact]
    public void ContextAndThreadBoundariesRejectRetargeting()
    {
        // Arrange: Capture the originating context and register its buffer.
        (nint, long) current = (1, 1);
        using var manager = new RHIResourceManager(() => current, static _ => true);
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        // Act / Assert: Context switching and foreign-thread registration are rejected.
        current = (2, 1);
        Assert.Throws<InvalidOperationException>(() => manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7));
        current = (1, 1);
        Exception? failure = null;
        var worker = new Thread(() => { try { manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7); } catch (Exception e) { failure = e; } });
        worker.Start(); worker.Join();
        Assert.IsType<InvalidOperationException>(failure);
        // Act / Assert: Context shutdown removes the entry and rejects further registration.
        manager.Dispose();
        Assert.Equal(GpuResourceRegistrationState.ContextRetired, entry.State);
        Assert.False(manager.IsRegistered(entry));
        Assert.Throws<ObjectDisposedException>(() => manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7));
    }

    /// <summary>Shutdown during a native read cannot deadlock the registry or publish into a retired entry.</summary>
    [Fact]
    public void RetirementDuringReadPreventsPublication()
    {
        // Arrange: Make the active reader retire its manager from a worker while holding the read callback.
        using var manager = CreateManager();
        var reader = new BufferReader();
        var entry = DiscoverPreexistingBuffer(manager, 7, reader)!;
        using var alias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, alias);
        Thread? worker = null;
        Exception? workerFailure = null;
        bool shutdownCompletedDuringRead = false;
        reader.OnRead = () =>
        {
            worker = new Thread(() =>
            {
                try { manager.Dispose(); }
                catch (Exception error) { workerFailure = error; }
            });
            worker.Start();
            shutdownCompletedDuringRead = worker.Join(TimeSpan.FromSeconds(5));
        };
        // Act: Refresh metadata while the read callback waits briefly for concurrent shutdown.
        try
        {
            Assert.Throws<ObjectDisposedException>(() => manager.RefreshMetadata(entry, GpuMetadataChange.Storage));
        }
        finally
        {
            // Let the read callback unwind before waiting for disposal, then join before manager cleanup.
            if (worker?.IsAlive == true) worker.Join();
        }
        // Assert: Shutdown completes during the read and reports no worker failure.
        Assert.NotNull(worker);
        Assert.True(shutdownCompletedDuringRead, "Registry shutdown must complete while the native reader is in progress.");
        Assert.Null(workerFailure);
        Assert.Equal(GpuResourceRegistrationState.ContextRetired, entry.State);
        Assert.False(entry.IsUsable);
        Assert.True(manager.IsResourceMetadataRetired(alias));
        Assert.False(manager.IsResourceMetadataAvailable(alias));
    }

    #endregion

    #region Publication semantics
    /// <summary>Repeated storage replacement advances storage revision even when represented dimensions are equal.</summary>
    [Fact]
    public void EqualStorageReplacementAdvancesRevisionsWithoutChangingEntry()
    {
        // Arrange: Discover a buffer with fixed dimensions and retain its registration.
        using var manager = CreateManager();
        var reader = new BufferReader();
        var entry = DiscoverPreexistingBuffer(manager, 7, reader)!;
        // Act: Publish the same storage facts twice with an explicit replacement signal.
        manager.PublishMetadata(entry, reader.Candidate, GpuMetadataChange.Storage);
        manager.PublishMetadata(entry, reader.Candidate, GpuMetadataChange.Storage);
        // Assert: Storage revisions advance while identity and dimensions remain stable.
        Assert.Equal(3ul, entry.MetadataRevision);
        Assert.Equal(2ul, entry.StorageRevision);
        Assert.Same(entry, manager.FindResource(GpuResourceKind.Buffer, 7));
    }

    /// <summary>Image projections share backing entries and reject absent selected images independently of other aliases.</summary>
    [Fact]
    public void ImageProjectionUsesCanonicalFactsAndLocalSelection()
    {
        // Arrange: Associate one present and one missing array-image selection with the same texture.
        using var manager = CreateManager();
        var entry = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 7);
        using var image = GpuFramebufferAttachment.FromExternalImage(7, false, TextureDimension.TwoArray, 1, 2, ProjectionSeed(TextureDimension.TwoArray,1));
        using var missing = GpuFramebufferAttachment.FromExternalImage(7, false, TextureDimension.TwoArray, 1, 9, ProjectionSeed(TextureDimension.TwoArray,1));
        manager.AssociateImageResource(entry, missing);
        manager.AssociateImageResource(entry, image);
        var levels = ImmutableArray.Create(new GpuTextureLevelMetadata(1, 0, 8, 4, 3, TextureFormat.Rgba16f, 0, true));
        var data = new GpuTextureMetadata(TextureDimension.TwoArray, levels, true, 2, 0, 1);
        // Act: Publish canonical texture levels for both aliases.
        manager.PublishMetadata(entry, data, GpuMetadataChange.Storage);
        // Assert: The selected image projects local dimensions without changing shared registration identity.
        Assert.Equal(new GpuImageMetadata(8, 4, 1, TextureFormat.Rgba16f, 0), new GpuImageMetadata(image.Width, image.Height, image.ImageDepth, image.InternalFormat, image.Samples));
        Assert.True(manager.IsResourceMetadataAvailable(image));
        Assert.False(manager.IsResourceMetadataAvailable(missing));
        Assert.Equal(2, entry.AssociationCount);
        Assert.Same(entry, manager.GetResourceRegistration(image));
    }

    /// <summary>A layered one-dimensional array has unit-height image planes and a separate layer extent.</summary>
    [Fact]
    public void LayeredOneDimensionalImagesDoNotUseLayerCountAsPlaneHeight()
    {
        // Arrange: Associate one image view with a three-layer one-dimensional array.
        using var manager = CreateManager();
        var entry = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 7);
        using var image = GpuFramebufferAttachment.FromExternalImage(7, false, TextureDimension.OneArray, 0, null, ProjectionSeed(TextureDimension.OneArray,0));
        manager.AssociateImageResource(entry, image);
        var levels = ImmutableArray.Create(new GpuTextureLevelMetadata(0, 0, 8, 3, 1, TextureFormat.Rgba8, 0, true));
        // Act: Publish the layered texture description.
        manager.PublishMetadata(entry, new(TextureDimension.OneArray, levels, true, 1, 0, 0), GpuMetadataChange.Storage);
        // Assert: Projection uses unit plane height and keeps layer count as depth.
        Assert.True(manager.IsResourceMetadataAvailable(image));
        Assert.Equal(new GpuImageMetadata(8, 1, 3, TextureFormat.Rgba8, 0), new GpuImageMetadata(image.Width, image.Height, image.ImageDepth, image.InternalFormat, image.Samples));
    }

    /// <summary>A whole cube image requires all six faces with matching dimensions, format and sample count.</summary>
    [Fact]
    public void WholeCubeProjectionRejectsMissingOrIncompatibleFaces()
    {
        // Arrange: Build six matching cube faces and select the whole image as one alias.
        using var manager = CreateManager();
        var entry = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 7);
        using var image = GpuFramebufferAttachment.FromExternalImage(7, false, TextureDimension.Cube, 0, null, ProjectionSeed(TextureDimension.Cube,0));
        manager.AssociateImageResource(entry, image);
        var faces = Enumerable.Range(0, 6).Select(face => new GpuTextureLevelMetadata(0,
            CubeFace.PositiveX + face, 8, 8, 1, TextureFormat.Rgba8, 0, true)).ToImmutableArray();
        var complete = new GpuTextureMetadata(TextureDimension.Cube, faces, false, 0, 0, 0);
        // Act: Publish a complete face set to establish the whole-cube projection.
        manager.PublishMetadata(entry, complete, GpuMetadataChange.Storage);
        // Assert: All six faces make the cube view available.
        Assert.True(manager.IsResourceMetadataAvailable(image));
        Assert.Equal(6, image.ImageDepth);

        // Act: Remove a face and verify the incomplete cube is unavailable.
        manager.PublishMetadata(entry, complete with { Levels = faces.RemoveAt(5) }, GpuMetadataChange.Storage);
        Assert.False(manager.IsResourceMetadataAvailable(image));
        // Act: Restore the face with mismatched dimensions and verify the cube remains unavailable.
        manager.PublishMetadata(entry, complete with { Levels = faces.SetItem(5, faces[5] with { Width = 4 }) }, GpuMetadataChange.Storage);
        Assert.False(manager.IsResourceMetadataAvailable(image));
        // Act: Restore the dimensions with a mismatched format and verify the cube remains unavailable.
        manager.PublishMetadata(entry, complete with { Levels = faces.SetItem(5, faces[5] with { Format = TextureFormat.Rgba16f }) }, GpuMetadataChange.Storage);
        Assert.False(manager.IsResourceMetadataAvailable(image));
        manager.PublishMetadata(entry, complete with { Levels = faces.SetItem(5, faces[5] with { Samples = 4 }) }, GpuMetadataChange.Storage);
        Assert.False(manager.IsResourceMetadataAvailable(image));
        manager.PublishMetadata(entry, complete, GpuMetadataChange.Storage);
        Assert.True(manager.IsResourceMetadataAvailable(image));
        Assert.Equal(new GpuImageMetadata(8, 8, 6, TextureFormat.Rgba8, 0),
            new GpuImageMetadata(image.Width, image.Height, image.ImageDepth, image.InternalFormat, image.Samples));
    }

    /// <summary>The public generic notification assigns only local snapshot data without changing canonical authority.</summary>
    [Fact]
    public void TypedNotificationDoesNotAlterCanonicalStateOrRevision()
    {
        // Arrange: Associate a wrapper with one canonical buffer snapshot.
        using var manager = CreateManager();
        var entry = DiscoverPreexistingBuffer(manager, 7, new BufferReader())!;
        using var resource = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, resource);
        var canonical = entry.Metadata;
        Assert.True(manager.IsResourceMetadataAvailable(resource));
        // Act / Assert: Assigning a local typed snapshot does not overwrite manager authority.
        resource.AssignMetadataSnapshot(new(100, BufferUsage.DynamicDraw));
        Assert.Equal(canonical, entry.Metadata);
        Assert.True(manager.IsResourceMetadataAvailable(resource));
        Assert.Equal(1ul, entry.MetadataRevision);
        // Act / Assert: A manager notification restores the canonical wrapper snapshot without advancing revision.
        manager.PublishMetadata(entry, canonical, GpuMetadataChange.Metadata);
        Assert.Equal(canonical, resource.Snapshot);
    }

    /// <summary>Recovery through acquisition advances publication revision after a failed metadata refresh.</summary>
    [Fact]
    public void AcquisitionRecoveryDoesNotReuseTheFailedPublicationRevision()
    {
        // Arrange: Discover one readable buffer and retain its canonical entry.
        using var manager = CreateManager();
        var reader = new BufferReader();
        var entry = DiscoverPreexistingBuffer(manager, 7, reader)!;
        // Act: A failed refresh withdraws the snapshot.
        reader.Readable = false;
        Assert.False(manager.RefreshMetadata(entry, GpuMetadataChange.Storage));
        // Act: Restore readability and recover the original native name.
        reader.Readable = true;
        Assert.Same(entry, manager.RecoverResource(GpuResourceKind.Buffer, 7, reader));
        // Assert: Recovery uses a fresh metadata revision and preserves storage history.
        Assert.Equal(3ul, entry.MetadataRevision);
        Assert.Equal(1ul, entry.StorageRevision);
    }

    /// <summary>Unregistration preserves the native object and allows the same live wrapper to subscribe again.</summary>
    [Fact]
    public void UnregisteredLiveResourceCanReceiveAnotherInitialSnapshot()
    {
        // Arrange: Attach one valid wrapper to an observed native buffer.
        using var manager = CreateManager();
        var entry = DiscoverPreexistingBuffer(manager, 7, new BufferReader())!;
        using var alias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, alias);
        // Act: Unregister the wrapper without disposing its native object.
        manager.UnregisterResource(entry, alias);
        // Assert: The wrapper remains valid but has no tracked snapshot.
        Assert.False(manager.IsResourceMetadataTracked(alias));
        Assert.False(manager.IsResourceMetadataAvailable(alias));
        Assert.True(alias.IsValid);
        // Act: Associate the same wrapper again with the original entry.
        manager.AssociateResource(entry, alias);
        // Assert: The wrapper receives the initial canonical snapshot again.
        Assert.True(manager.IsResourceMetadataTracked(alias));
        Assert.True(manager.IsResourceMetadataAvailable(alias));
        Assert.Equal(entry.Metadata, alias.Snapshot);
        Assert.Same(entry, manager.GetResourceRegistration(alias));
    }

    /// <summary>Old entry cleanup and direct snapshot assignment cannot revive retired manager associations.</summary>
    [Fact]
    public void RetiredAssociationRejectsSnapshotAssignmentAndReusedNameRegistration()
    {
        // Arrange: Retire an original name, then attach a new wrapper to its replacement.
        using var manager = CreateManager();
        var reader = new BufferReader();
        var oldEntry = DiscoverPreexistingBuffer(manager, 7, reader)!;
        using var oldAlias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(oldEntry, oldAlias);
        manager.ObserveDeletion(GpuResourceKind.Buffer, 7);
        var replacement = DiscoverPreexistingBuffer(manager, 7, reader)!;
        using var newAlias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(replacement, newAlias);
        // Act: Attempt stale cleanup and local assignment against the retired wrapper.
        manager.UnregisterResource(oldEntry, oldAlias);
        manager.UnregisterResource(oldEntry, newAlias);
        oldAlias.AssignMetadataSnapshot(new(100, BufferUsage.DynamicDraw));
        // Assert: Stale cleanup cannot revive the old wrapper or disturb the replacement association.
        Assert.True(manager.IsResourceMetadataRetired(oldAlias));
        Assert.False(manager.IsResourceMetadataAvailable(oldAlias));
        Assert.Throws<InvalidOperationException>(() => manager.AssociateResource(replacement, oldAlias));
        Assert.True(manager.IsResourceMetadataAvailable(newAlias));
        Assert.True(newAlias.IsValid);
        Assert.Equal(1, replacement.AssociationCount);
    }

    #endregion

    #region Worker retirement
    /// <summary>Worker retirement only marks the captured entry unavailable and cannot mutate another registration.</summary>
    [Fact]
    public void WorkerRetirementPreservesOriginalRegistrationUntilDeletion()
    {
        // Arrange: Discover one live buffer and associate a wrapper before worker retirement.
        using var manager = CreateManager();
        var entry = DiscoverPreexistingBuffer(manager, 7, new BufferReader())!;
        using var alias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, alias);
        bool retired = false;
        // Act: Retire the original entry on a worker and always join before inspecting manager state.
        Exception? workerFailure = null;
        var worker = new Thread(() =>
        {
            try { retired = manager.RetireResource(entry); }
            catch (Exception error) { workerFailure = error; }
        });
        worker.Start();
        try { worker.Join(); }
        finally { if (worker.IsAlive) worker.Join(); }
        // Assert: Worker retirement changes the entry state but leaves registry mutation to the owner thread.
        Assert.Null(workerFailure);
        Assert.True(retired);
        Assert.False(manager.IsResourceMetadataAvailable(alias));
        Assert.True(manager.IsResourceMetadataRetired(alias));
        alias.AssignMetadataSnapshot(new(100, BufferUsage.DynamicDraw));
        Assert.False(manager.IsResourceMetadataAvailable(alias));
        Assert.True(manager.IsRegistered(entry));
        Assert.Throws<InvalidOperationException>(() => manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7));
        // Act: Observe the native deletion on the owner thread.
        manager.ObserveDeletion(GpuResourceKind.Buffer, 7);
        // Assert: Only deletion removes the retired registration.
        Assert.False(manager.IsRegistered(entry));
    }

    /// <summary>Parallel identity lookup and duplicate removal do not discard canonical metadata or retain aliases.</summary>
    [Fact]
    public void ConcurrentLookupsAndUnregisterPreserveCanonicalEntry()
    {
        // Arrange: Associate many wrappers with one canonical buffer entry.
        using var manager = CreateManager();
        var entry = DiscoverPreexistingBuffer(manager, 7, new BufferReader())!;
        var resources = Enumerable.Range(0, 64).Select(_ => new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7)).ToArray();
        foreach (var resource in resources) manager.AssociateResource(entry, resource);
        // Act: Look up the canonical entry while workers remove duplicate associations.
        Parallel.For(0, 256, index =>
        {
            Assert.Same(entry, manager.FindResource(GpuResourceKind.Buffer, 7));
            manager.UnregisterResource(entry, resources[index % resources.Length]);
        });
        // Assert: All associations are removed while canonical entry metadata remains usable.
        Assert.Equal(0, entry.AssociationCount);
        Assert.True(entry.IsUsable);
        foreach (var resource in resources)
        {
            Assert.False(manager.IsResourceMetadataTracked(resource));
            Assert.False(manager.IsResourceMetadataAvailable(resource));
            resource.Dispose();
        }
    }

    /// <summary>Asynchronous recipient removal can overlap publication without changing the remaining alias's snapshot.</summary>
    [Fact]
    public void ConcurrentRemovalDuringPublicationPreservesSurvivingAlias()
    {
        // Arrange: Keep one alias alive while a worker prepares to remove many transient recipients.
        using var manager = CreateManager();
        var entry = DiscoverPreexistingBuffer(manager, 7, new BufferReader())!;
        using var survivor = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, survivor);
        var resources = Enumerable.Range(0, 128).Select(_ => new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7)).ToArray();
        foreach (var resource in resources) manager.AssociateResource(entry, resource);
        using var start = new ManualResetEventSlim();
        var cancellationToken = TestContext.Current.CancellationToken;
        Exception? workerFailure = null;
        var worker = new Thread(() =>
        {
            try
            {
                start.Wait(cancellationToken);
                foreach (var resource in resources) manager.UnregisterResource(entry, resource);
            }
            catch (Exception error) { workerFailure = error; }
        });
        worker.Start();
        try
        {
            start.Set();
            // Act: Publish changing snapshots while the worker unregisters transient recipients.
            for (int index = 1; index <= 100; index++)
                manager.PublishMetadata(entry, new(index, BufferUsage.DynamicDraw), GpuMetadataChange.Metadata);
        }
        finally
        {
            start.Set();
            worker.Join();
        }
        // Assert: The survivor receives the final snapshot and all transient wrappers are detached.
        Assert.Null(workerFailure);
        Assert.Equal(1, entry.AssociationCount);
        Assert.True(manager.IsResourceMetadataAvailable(survivor));
        Assert.Equal(new GpuBufferMetadata(100, BufferUsage.DynamicDraw), survivor.Snapshot);
        foreach (var resource in resources)
        {
            Assert.False(manager.IsResourceMetadataTracked(resource));
            Assert.False(manager.IsResourceMetadataAvailable(resource));
            resource.Dispose();
        }
    }

    /// <summary>Context retirement safely removes entries while other threads look up their identities.</summary>
    [Fact]
    public void ConcurrentLookupCannotKeepRetiredContextEntriesRegistered()
    {
        // Arrange: Register a bounded set of names and start a worker performing identity lookups.
        using var manager = CreateManager();
        var entries = Enumerable.Range(1, 64)
            .Select(id => manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, id)).ToArray();
        using var ready = new ManualResetEventSlim();
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            ready.Set();
            try
            {
                for (int index = 0; index < 4096; index++) manager.FindResource(GpuResourceKind.Buffer, index % 64 + 1);
            }
            catch (ObjectDisposedException) { }
            catch (Exception error) { failure = error; }
        });
        worker.Start();
        // Act: Retire the manager while lookups are in flight, even if waiting for worker readiness is canceled.
        try { ready.Wait(TestContext.Current.CancellationToken); }
        finally
        {
            try { manager.Dispose(); }
            finally { worker.Join(); }
        }
        // Assert: No lookup failed unexpectedly and all original entries are removed.
        Assert.Null(failure);
        foreach (var entry in entries)
        {
            Assert.False(manager.IsRegistered(entry));
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, entry.State);
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Models the explicit pre-install owner handoff for deterministic initial-discovery tests.</summary>
    private static GpuResourceMetadataEntry<GpuBufferMetadata>? DiscoverPreexistingBuffer(RHIResourceManager manager,
        nint id, IGpuResourceMetadataReader<GpuBufferMetadata> reader)
    {
        if (manager.FindResource(GpuResourceKind.Buffer, id) is null) manager.RecordPreexistingBuffer(id);
        return manager.AcquireResource(GpuResourceKind.Buffer, id, reader);
    }

    /// <summary>Supplies initial fixture image facts without discovering a nonexistent native object.</summary>
    private static GpuTextureMetadata ProjectionSeed(TextureDimension target,int level)
    {
        var levels=target==TextureDimension.Cube
            ? Enumerable.Range(0,6).Select(face=>new GpuTextureLevelMetadata(level,CubeFace.PositiveX+face,8,8,1,TextureFormat.Rgba8,0,true)).ToImmutableArray()
            : ImmutableArray.Create(new GpuTextureLevelMetadata(level,0,8,target==TextureDimension.OneArray?10:4,10,TextureFormat.Rgba8,0,true));
        return new(target,levels,true,level+1,0,level);
    }

    /// <summary>Creates a context-scoped registry without loading or invoking native graphics.</summary>
    private static RHIResourceManager CreateManager() => new(static () => (1, 1), static _ => true);

    /// <summary>Associates a small batch and ends their strong-reference scope before collection.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] AssociateTemporaryBatch(RHIResourceManager manager, GpuResourceMetadataEntry<GpuBufferMetadata> entry)
    {
        var abandoned = new WeakReference[8];
        for (int index = 0; index < abandoned.Length; index++)
        {
            var resource = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
            manager.AssociateResource(entry, resource);
            abandoned[index] = new(resource);
        }
        return abandoned;
    }

    /// <summary>Models a validated native metadata reader with observable failure and query behavior.</summary>
    private sealed class BufferReader : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public bool IsResource(nint id) { ValidityChecks++; return Exists; }

        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata data)
        {
            Reads++;
            OnRead?.Invoke();
            if (Throws) throw new InvalidOperationException("Injected metadata read failure.");
            data = Candidate;
            return IsResource(id) && Readable;
        }
        #endregion

        #region Internal API
        internal bool Exists = true, Readable = true, Throws;
        internal int Reads, ValidityChecks;
        internal Action? OnRead;
        internal GpuBufferMetadata Candidate = new(16, BufferUsage.StaticDraw);
        #endregion
    }

    /// <summary>Exposes typed snapshot state without native ownership or driver interaction.</summary>
    private sealed class Resource<TMetadata>(GpuResourceKind kind, nint id) : GpuResource<TMetadata> where TMetadata : struct
    {
        #region Public API
        public override nint ResourceId { get; protected set; } = id;

        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
        #endregion

        #region Protected/Internal
        internal TMetadata Snapshot => metadata;
        protected override GpuResourceKind ResourceKind => kind;
        protected override bool OwnsResource => false;
        #endregion
    }
    #endregion
}
