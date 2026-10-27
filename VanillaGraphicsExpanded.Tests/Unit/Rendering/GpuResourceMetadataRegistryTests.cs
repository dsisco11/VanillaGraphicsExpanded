using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies canonical metadata identity, publication and retention without requiring native commands.</summary>
public sealed class GpuResourceMetadataRegistryTests
{
    #region Public API
    #region Acquisition and refresh
    /// <summary>Each actual snapshot acquisition validates identity exactly once inside its reader.</summary>
    [Fact]
    public void AcquisitionAndRefreshValidateIdentityOncePerSnapshot()
    {
        using var manager = CreateManager();
        var reader = new BufferReader();
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
        Assert.Equal(1, reader.ValidityChecks);
        Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
        Assert.Equal(1, reader.ValidityChecks);
        Assert.True(manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
        Assert.Equal(2, reader.ValidityChecks);
        reader.Exists = false;
        Assert.False(manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
        Assert.Equal(3, reader.ValidityChecks);
        Assert.False(entry.MetadataAvailable);
        Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, 8, reader));
        Assert.Equal(4, reader.ValidityChecks);
        Assert.Null(manager.FindResource(GpuResourceKind.Buffer, 8));
    }

    /// <summary>External acquisition captures once and each live alias receives the same canonical facts.</summary>
    [Fact]
    public void AcquisitionSharesEntryAndRefreshesAllAliasesOnce()
    {
        using var manager = CreateManager();
        var reader = new BufferReader { Candidate = new(24, BufferUsageHint.DynamicDraw) };
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
        Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
        using var first = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var second = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, first);
        manager.AssociateResource(entry, second);
        Assert.Equal(reader.Candidate, first.Snapshot);
        Assert.Equal(first.Snapshot, second.Snapshot);
        Assert.True(manager.IsResourceMetadataAvailable(first));
        Assert.Equal(1, reader.Reads);
        reader.Candidate = new(96, BufferUsageHint.StaticDraw);
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

    /// <summary>Reserved names remain unavailable until a reader proves realization and complete metadata.</summary>
    [Fact]
    public void CreatedNamesDoNotInventStorageAndCanLaterBeAcquired()
    {
        using var manager = CreateManager();
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var resource = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, resource);
        Assert.False(manager.IsResourceMetadataAvailable(resource));
        var reader = new BufferReader { Exists = false };
        Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
        Assert.Equal(1, reader.Reads);
        Assert.Equal(1, reader.ValidityChecks);
        reader.Exists = true;
        Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
        Assert.True(manager.IsResourceMetadataAvailable(resource));
    }

    /// <summary>Failed or throwing readers disable all aliases and later complete refreshes recover them.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshFailureCannotExposeStaleSnapshots(bool throws)
    {
        using var manager = CreateManager();
        var reader = new BufferReader();
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
        using var resource = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, resource);
        reader.Readable = false; reader.Throws = throws;
        if (throws) Assert.Throws<InvalidOperationException>(() => manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
        else Assert.False(manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
        Assert.False(entry.MetadataAvailable);
        Assert.False(manager.IsResourceMetadataAvailable(resource));
        Assert.Equal(1ul, entry.MetadataRevision);
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
        using var manager = CreateManager();
        var reader = new BufferReader();
        var old = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
        using var alias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(old, alias);
        manager.ObserveDeletion(GpuResourceKind.Buffer, 7);
        var replacement = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
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
        using var first = CreateManager();
        using var second = new GpuResourceManager(static () => (2, 1), static _ => true);
        nint id = nint.Size == 8 ? unchecked((nint)0x123456789L) : 12345;
        var sync = first.RegisterCreatedResource<GpuSyncMetadata>(GpuResourceKind.Sync, id);
        var buffer = first.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, id);
        var other = second.RegisterCreatedResource<GpuSyncMetadata>(GpuResourceKind.Sync, id);
        Assert.Equal(id, sync.Id);
        Assert.NotSame(sync, buffer);
        Assert.NotSame(sync, other);
        Assert.Same(sync, first.FindResource(GpuResourceKind.Sync, id));
        Assert.Throws<ArgumentException>(() => first.RegisterCreatedResource<GpuSyncMetadata>(GpuResourceKind.Texture, id));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.RegisterCreatedResource<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer, 0));
        using var alias = new Resource<GpuSyncMetadata>(GpuResourceKind.Sync, id);
        Assert.Throws<InvalidOperationException>(() => second.AssociateResource(sync, alias));
    }

    /// <summary>Explicit removal and wrapper collection preserve the live object's canonical entry.</summary>
    [Fact]
    public void WeakAssociationsArePrunedWithoutDiscardingMetadata()
    {
        using var manager = CreateManager();
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, new BufferReader())!;
        var weak = AssociateTemporary(manager, entry);
        for (int attempt = 0; attempt < 5 && weak.IsAlive; attempt++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.False(weak.IsAlive);
        Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, 7, new BufferReader()));
        Assert.Equal(0, entry.AssociationCount);
        using var resource = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, resource);
        manager.UnregisterResource(entry, resource);
        Assert.False(manager.IsResourceMetadataTracked(resource));
        Assert.False(manager.IsResourceMetadataAvailable(resource));
        Assert.True(entry.IsUsable);
        Assert.Same(entry, manager.FindResource(GpuResourceKind.Buffer, 7));
    }

    #endregion

    #region Guards and context boundaries
    /// <summary>Independent pass and lifetime guards belong to shared entries and remain releasable after retirement.</summary>
    [Fact]
    public void SharedGuardsAndRetirementDoNotCreateReplacementIdentity()
    {
        using var manager = CreateManager();
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        entry.Retain(true);
        Assert.Throws<InvalidOperationException>(() => entry.RequireMutableStorage());
        Assert.Throws<InvalidOperationException>(() => manager.RetireResource(entry));
        entry.Release(true); entry.Retain(false);
        entry.RequireMutableStorage();
        Assert.Throws<InvalidOperationException>(() => manager.RetireResource(entry));
        entry.Release(false);
        Assert.True(manager.RetireResource(entry));
        Assert.False(manager.RetireResource(entry));
        Assert.False(entry.IsUsable);
        Assert.True(manager.IsRegistered(entry));
        Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, 7, new BufferReader()));
        Assert.Throws<ObjectDisposedException>(() => entry.Retain(true));
    }

    /// <summary>Native mutations remain context-thread confined while registration identity can be looked up concurrently.</summary>
    [Fact]
    public void ContextAndThreadBoundariesRejectRetargeting()
    {
        (nint, long) current = (1, 1);
        using var manager = new GpuResourceManager(() => current, static _ => true);
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        current = (2, 1);
        Assert.Throws<InvalidOperationException>(() => manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7));
        current = (1, 1);
        Exception? failure = null;
        var worker = new Thread(() => { try { manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7); } catch (Exception e) { failure = e; } });
        worker.Start(); worker.Join();
        Assert.IsType<InvalidOperationException>(failure);
        manager.Dispose();
        Assert.Equal(GpuResourceRegistrationState.ContextRetired, entry.State);
        Assert.False(manager.IsRegistered(entry));
        Assert.Throws<ObjectDisposedException>(() => manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7));
    }

    /// <summary>Shutdown during a native read cannot deadlock the registry or publish into a retired entry.</summary>
    [Fact]
    public void RetirementDuringReadPreventsPublication()
    {
        using var manager = CreateManager();
        var reader = new BufferReader();
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
        using var alias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, alias);
        reader.OnRead = () =>
        {
            var worker = new Thread(manager.Dispose);
            worker.Start();
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "Registry shutdown must complete while a native reader is in progress.");
        };
        Assert.Throws<ObjectDisposedException>(() => manager.RefreshMetadata(entry, GpuMetadataChange.Storage));
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
        using var manager = CreateManager();
        var reader = new BufferReader();
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
        manager.PublishMetadata(entry, reader.Candidate, GpuMetadataChange.Storage);
        manager.PublishMetadata(entry, reader.Candidate, GpuMetadataChange.Storage);
        Assert.Equal(3ul, entry.MetadataRevision);
        Assert.Equal(2ul, entry.StorageRevision);
        Assert.Same(entry, manager.FindResource(GpuResourceKind.Buffer, 7));
    }

    /// <summary>Image projections share backing entries and reject absent selected images independently of other aliases.</summary>
    [Fact]
    public void ImageProjectionUsesCanonicalFactsAndLocalSelection()
    {
        using var manager = CreateManager();
        var entry = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 7);
        using var image = GpuFramebufferAttachment.FromExternalImage(7, false, TextureTarget.Texture2DArray, 1, 2, ProjectionSeed(TextureTarget.Texture2DArray,1));
        using var missing = GpuFramebufferAttachment.FromExternalImage(7, false, TextureTarget.Texture2DArray, 1, 9, ProjectionSeed(TextureTarget.Texture2DArray,1));
        manager.AssociateImageResource(entry, missing);
        manager.AssociateImageResource(entry, image);
        var levels = ImmutableArray.Create(new GpuTextureLevelMetadata(1, 0, 8, 4, 3, PixelInternalFormat.Rgba16f, 0, true));
        var data = new GpuTextureMetadata(TextureTarget.Texture2DArray, levels, true, 2, 0, 1);
        manager.PublishMetadata(entry, data, GpuMetadataChange.Storage);
        Assert.Equal(new GpuImageMetadata(8, 4, 1, PixelInternalFormat.Rgba16f, 0), new GpuImageMetadata(image.Width, image.Height, image.ImageDepth, image.InternalFormat, image.Samples));
        Assert.True(manager.IsResourceMetadataAvailable(image));
        Assert.False(manager.IsResourceMetadataAvailable(missing));
        Assert.Equal(2, entry.AssociationCount);
        Assert.Same(entry, manager.GetResourceRegistration(image));
    }

    /// <summary>A layered one-dimensional array has unit-height image planes and a separate layer extent.</summary>
    [Fact]
    public void LayeredOneDimensionalImagesDoNotUseLayerCountAsPlaneHeight()
    {
        using var manager = CreateManager();
        var entry = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 7);
        using var image = GpuFramebufferAttachment.FromExternalImage(7, false, TextureTarget.Texture1DArray, 0, null, ProjectionSeed(TextureTarget.Texture1DArray,0));
        manager.AssociateImageResource(entry, image);
        var levels = ImmutableArray.Create(new GpuTextureLevelMetadata(0, 0, 8, 3, 1, PixelInternalFormat.Rgba8, 0, true));
        manager.PublishMetadata(entry, new(TextureTarget.Texture1DArray, levels, true, 1, 0, 0), GpuMetadataChange.Storage);
        Assert.True(manager.IsResourceMetadataAvailable(image));
        Assert.Equal(new GpuImageMetadata(8, 1, 3, PixelInternalFormat.Rgba8, 0), new GpuImageMetadata(image.Width, image.Height, image.ImageDepth, image.InternalFormat, image.Samples));
    }

    /// <summary>A whole cube image requires all six faces with matching dimensions, format and sample count.</summary>
    [Fact]
    public void WholeCubeProjectionRejectsMissingOrIncompatibleFaces()
    {
        using var manager = CreateManager();
        var entry = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 7);
        using var image = GpuFramebufferAttachment.FromExternalImage(7, false, TextureTarget.TextureCubeMap, 0, null, ProjectionSeed(TextureTarget.TextureCubeMap,0));
        manager.AssociateImageResource(entry, image);
        var faces = Enumerable.Range(0, 6).Select(face => new GpuTextureLevelMetadata(0,
            TextureTarget.TextureCubeMapPositiveX + face, 8, 8, 1, PixelInternalFormat.Rgba8, 0, true)).ToImmutableArray();
        var complete = new GpuTextureMetadata(TextureTarget.TextureCubeMap, faces, false, 0, 0, 0);
        manager.PublishMetadata(entry, complete, GpuMetadataChange.Storage);
        Assert.True(manager.IsResourceMetadataAvailable(image));
        Assert.Equal(6, image.ImageDepth);

        // Each rejected publication retains canonical facts without exposing a usable whole-cube alias.
        manager.PublishMetadata(entry, complete with { Levels = faces.RemoveAt(5) }, GpuMetadataChange.Storage);
        Assert.False(manager.IsResourceMetadataAvailable(image));
        manager.PublishMetadata(entry, complete with { Levels = faces.SetItem(5, faces[5] with { Width = 4 }) }, GpuMetadataChange.Storage);
        Assert.False(manager.IsResourceMetadataAvailable(image));
        manager.PublishMetadata(entry, complete with { Levels = faces.SetItem(5, faces[5] with { Format = PixelInternalFormat.Rgba16f }) }, GpuMetadataChange.Storage);
        Assert.False(manager.IsResourceMetadataAvailable(image));
        manager.PublishMetadata(entry, complete with { Levels = faces.SetItem(5, faces[5] with { Samples = 4 }) }, GpuMetadataChange.Storage);
        Assert.False(manager.IsResourceMetadataAvailable(image));
        manager.PublishMetadata(entry, complete, GpuMetadataChange.Storage);
        Assert.True(manager.IsResourceMetadataAvailable(image));
        Assert.Equal(new GpuImageMetadata(8, 8, 6, PixelInternalFormat.Rgba8, 0),
            new GpuImageMetadata(image.Width, image.Height, image.ImageDepth, image.InternalFormat, image.Samples));
    }

    /// <summary>The public generic notification assigns only local snapshot data without changing canonical authority.</summary>
    [Fact]
    public void TypedNotificationDoesNotAlterCanonicalStateOrRevision()
    {
        using var manager = CreateManager();
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, new BufferReader())!;
        using var resource = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, resource);
        var canonical = entry.Metadata;
        Assert.True(manager.IsResourceMetadataAvailable(resource));
        resource.HandleMetadataChanged(new(100, BufferUsageHint.DynamicDraw));
        Assert.Equal(canonical, entry.Metadata);
        Assert.True(manager.IsResourceMetadataAvailable(resource));
        Assert.Equal(1ul, entry.MetadataRevision);
        manager.PublishMetadata(entry, canonical, GpuMetadataChange.Metadata);
        Assert.Equal(canonical, resource.Snapshot);
    }

    /// <summary>Recovery through acquisition advances publication revision after a failed metadata refresh.</summary>
    [Fact]
    public void AcquisitionRecoveryDoesNotReuseTheFailedPublicationRevision()
    {
        using var manager = CreateManager();
        var reader = new BufferReader();
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
        reader.Readable = false;
        Assert.False(manager.RefreshMetadata(entry, GpuMetadataChange.Storage));
        reader.Readable = true;
        Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
        Assert.Equal(2ul, entry.MetadataRevision);
        Assert.Equal(1ul, entry.StorageRevision);
    }

    /// <summary>Unregistration preserves the native object and allows the same live wrapper to subscribe again.</summary>
    [Fact]
    public void UnregisteredLiveResourceCanReceiveAnotherInitialSnapshot()
    {
        using var manager = CreateManager();
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, new BufferReader())!;
        using var alias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, alias);
        manager.UnregisterResource(entry, alias);
        Assert.False(manager.IsResourceMetadataTracked(alias));
        Assert.False(manager.IsResourceMetadataAvailable(alias));
        Assert.True(alias.IsValid);
        manager.AssociateResource(entry, alias);
        Assert.True(manager.IsResourceMetadataTracked(alias));
        Assert.True(manager.IsResourceMetadataAvailable(alias));
        Assert.Equal(entry.Metadata, alias.Snapshot);
        Assert.Same(entry, manager.GetResourceRegistration(alias));
    }

    /// <summary>Old entry cleanup and direct snapshot assignment cannot revive retired manager associations.</summary>
    [Fact]
    public void RetiredAssociationRejectsSnapshotAssignmentAndReusedNameRegistration()
    {
        using var manager = CreateManager();
        var reader = new BufferReader();
        var oldEntry = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
        using var oldAlias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(oldEntry, oldAlias);
        manager.ObserveDeletion(GpuResourceKind.Buffer, 7);
        var replacement = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
        using var newAlias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(replacement, newAlias);
        manager.UnregisterResource(oldEntry, oldAlias);
        manager.UnregisterResource(oldEntry, newAlias);
        oldAlias.HandleMetadataChanged(new(100, BufferUsageHint.DynamicDraw));
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
        using var manager = CreateManager();
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, new BufferReader())!;
        using var alias = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, alias);
        bool retired = false;
        var worker = new Thread(() => retired = manager.RetireResource(entry));
        worker.Start(); worker.Join();
        Assert.True(retired);
        Assert.False(manager.IsResourceMetadataAvailable(alias));
        Assert.True(manager.IsResourceMetadataRetired(alias));
        alias.HandleMetadataChanged(new(100, BufferUsageHint.DynamicDraw));
        Assert.False(manager.IsResourceMetadataAvailable(alias));
        Assert.True(manager.IsRegistered(entry));
        Assert.Throws<InvalidOperationException>(() => manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7));
        manager.ObserveDeletion(GpuResourceKind.Buffer, 7);
        Assert.False(manager.IsRegistered(entry));
    }

    /// <summary>Parallel identity lookup and duplicate removal do not discard canonical metadata or retain aliases.</summary>
    [Fact]
    public void ConcurrentLookupsAndUnregisterPreserveCanonicalEntry()
    {
        using var manager = CreateManager();
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, new BufferReader())!;
        var resources = Enumerable.Range(0, 64).Select(_ => new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7)).ToArray();
        foreach (var resource in resources) manager.AssociateResource(entry, resource);
        Parallel.For(0, 256, index =>
        {
            Assert.Same(entry, manager.FindResource(GpuResourceKind.Buffer, 7));
            manager.UnregisterResource(entry, resources[index % resources.Length]);
        });
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
        using var manager = CreateManager();
        var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, new BufferReader())!;
        using var survivor = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, survivor);
        var resources = Enumerable.Range(0, 128).Select(_ => new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7)).ToArray();
        foreach (var resource in resources) manager.AssociateResource(entry, resource);
        using var start = new ManualResetEventSlim();
        var cancellationToken = TestContext.Current.CancellationToken;
        var worker = new Thread(() =>
        {
            start.Wait(cancellationToken);
            foreach (var resource in resources) manager.UnregisterResource(entry, resource);
        });
        worker.Start(); start.Set();
        for (int index = 1; index <= 100; index++)
            manager.PublishMetadata(entry, new(index, BufferUsageHint.DynamicDraw), GpuMetadataChange.Metadata);
        worker.Join();
        Assert.Equal(1, entry.AssociationCount);
        Assert.True(manager.IsResourceMetadataAvailable(survivor));
        Assert.Equal(new GpuBufferMetadata(100, BufferUsageHint.DynamicDraw), survivor.Snapshot);
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
        worker.Start(); ready.Wait(TestContext.Current.CancellationToken);
        manager.Dispose(); worker.Join();
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
    /// <summary>Supplies initial fixture image facts without discovering a nonexistent native object.</summary>
    private static GpuTextureMetadata ProjectionSeed(TextureTarget target,int level)
    {
        var levels=target==TextureTarget.TextureCubeMap
            ? Enumerable.Range(0,6).Select(face=>new GpuTextureLevelMetadata(level,TextureTarget.TextureCubeMapPositiveX+face,8,8,1,PixelInternalFormat.Rgba8,0,true)).ToImmutableArray()
            : ImmutableArray.Create(new GpuTextureLevelMetadata(level,0,8,target==TextureTarget.Texture1DArray?10:4,10,PixelInternalFormat.Rgba8,0,true));
        return new(target,levels,true,level+1,0,level);
    }

    /// <summary>Creates a context-scoped registry without loading or invoking native graphics.</summary>
    private static GpuResourceManager CreateManager() => new(static () => (1, 1), static _ => true);

    /// <summary>Ends the local strong-reference scope so collection tests measure registry retention.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AssociateTemporary(GpuResourceManager manager, GpuResourceMetadataEntry<GpuBufferMetadata> entry)
    {
        var resource = new Resource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, resource);
        return new(resource);
    }

    /// <summary>Models a validated native metadata reader with observable failure and query behavior.</summary>
    private sealed class BufferReader : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        internal bool Exists = true, Readable = true, Throws;
        internal int Reads, ValidityChecks;
        internal Action? OnRead;
        internal GpuBufferMetadata Candidate = new(16, BufferUsageHint.StaticDraw);

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
    }

    /// <summary>Exposes typed snapshot state without native ownership or driver interaction.</summary>
    private sealed class Resource<TMetadata>(GpuResourceKind kind, nint id) : GpuResource<TMetadata> where TMetadata : struct
    {
        internal TMetadata Snapshot => metadata;
        public override nint ResourceId { get; protected set; } = id;
        protected override GpuResourceKind ResourceKind => kind;
        protected override bool OwnsResource => false;

        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
    }
    #endregion
}
