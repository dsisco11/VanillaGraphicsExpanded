using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks explicitly composed operations, original identity and canonical delivery.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuMetadataOperationFoundationTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Operation completion

    /// <summary>Explicit participation publishes once and registered aliases receive the same canonical result.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitOperationPublishesOnce(bool registered)
    {
        // Arrange: Register the owner and optional alias, then subscribe to canonical metadata publication.
        // Act: Require owner participation, resize native storage, and commit one metadata result twice.
        // Assert: Aliases receive one committed capacity/revision update and the duplicate commit is rejected.
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        else fixture.RetireResourceServices();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            using var alias = new Alias(owner.BufferId);
            var manager = RHIResourceManagerSystem.CurrentManager;
            var entry = manager?.GetResourceRegistration(owner) as GpuResourceMetadataEntry<GpuBufferMetadata>;
            if (entry is not null) manager!.AssociateResource(entry, alias);
            ulong revision = entry?.MetadataRevision ?? 0, storage = entry?.StorageRevision ?? 0;
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                operation.RequireParticipation(owner, GpuMetadataChange.Metadata);
                ResizeNative(owner, 64);
                Assert.Equal(16, owner.SizeBytes);
                if (entry is not null) Assert.False(entry.MetadataAvailable);
                operation.Commit(new(64, BufferUsage.StaticDraw));
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(128, BufferUsage.DynamicDraw)));
                Assert.Equal(64, owner.SizeBytes);
                if (entry is not null) Assert.Equal((nint)64, alias.Snapshot.Capacity);
            }
            if (entry is not null)
            {
                Assert.True(entry.MetadataAvailable);
                Assert.Equal(revision + 1, entry.MetadataRevision);
                Assert.Equal(storage + 1, entry.StorageRevision);
            }
            OpenGlStateCache.Current.RequireResourceOperationAvailable(GpuResourceKind.Buffer, owner.BufferId);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    /// <summary>A successful explicit reader runs once; disposal performs no additional recovery.</summary>
    [Fact]
    public void OwnerChoosesRecoveryAndCommitsItsResult()
    {
        // Arrange: Give the registered owner a counting metadata reader for the recovery candidate.
        // Act: Resize native storage, read one candidate, and commit it through the explicit operation.
        // Assert: The reader runs once and the owner exposes the committed capacity after disposal.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            var reader = new CountingReader();
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                ResizeNative(owner, 64);
                Assert.True(reader.TryRead(owner.BufferId, out var candidate));
                operation.Commit(candidate);
            }
            Assert.Equal(1, reader.Reads);
            Assert.Equal(64, owner.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
    #region Interruption and recovery

    /// <summary>Disposal never invokes a reader; recovery is a later explicit owner decision.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedOperationDoesNotRecoverOnDispose(bool registered)
    {
        // Arrange an owner, reader, and optional registry entry to observe interruption and later recovery.
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        else fixture.RetireResourceServices();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            var manager = RHIResourceManagerSystem.CurrentManager;
            var entry = manager?.GetResourceRegistration(owner) as GpuResourceMetadataEntry<GpuBufferMetadata>;
            var reader = new CountingReader();
            if (entry is not null) entry.Reader = reader;
            ulong storage = entry?.StorageRevision ?? 0;
            // Act by interrupting the participating operation after native storage has changed.
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Metadata))
            {
                operation.RequireParticipation(owner, GpuMetadataChange.Storage);
                ResizeNative(owner, 64);
                try { throw new InvalidOperationException("Participating dispatch failed."); }
                catch (InvalidOperationException) { operation.Interrupt(); }
                Assert.Throws<InvalidOperationException>(() => owner.Allocate(128));
                using var alias = new Alias(owner.BufferId);
                Assert.Throws<InvalidOperationException>(() => new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(alias, GpuMetadataChange.Metadata));
                Assert.True(new GpuBufferMetadataReader().TryRead(owner.BufferId, out var unchanged));
                Assert.Equal((nint)64, unchanged.Capacity);
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(64, BufferUsage.StaticDraw)));
            }
            // Assert disposal did not recover; only the explicit owner recovery below may restore the metadata.
            Assert.Equal(0, reader.Reads);
            if (entry is not null) Assert.False(entry.MetadataAvailable);
            else Assert.Equal(0, owner.SizeBytes);
            // Act through the explicit recovery API after the interrupted operation has been disposed.
            RHIResourceManager.RecoverWrapperMetadata(owner, reader);
            // Assert exactly one recovery published the actual native capacity.
            Assert.Equal(1, reader.Reads);
            Assert.Equal(64, owner.SizeBytes);
            if (entry is not null) Assert.Equal(storage + 1, entry.StorageRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    /// <summary>A rejected alias operation cannot dispatch or join; an independent resource remains usable.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedAliasMutationIsRejectedBeforeDispatch(bool registered)
    {
        // Arrange: Create related aliases and an unrelated resource, selecting which alias attempts mutation.
        // Act: Attempt the alias operation, mutate the owner, and independently resize/read the unrelated resource.
        // Assert: Alias/owner conflicts throw while the unrelated resource commits its new capacity and the owner retains its old facts.
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        else fixture.RetireResourceServices();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            using var alias = new Alias(owner.BufferId);
            using var independent = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); independent.Allocate(8);
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Metadata))
            {
                Assert.Throws<InvalidOperationException>(() => new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(alias, GpuMetadataChange.Storage));
                Assert.Throws<InvalidOperationException>(() => owner.Allocate(128));
                independent.Allocate(32);
                Assert.Equal(32, independent.SizeBytes);
                Assert.True(new GpuBufferMetadataReader().TryRead(owner.BufferId, out var actual));
                Assert.Equal((nint)16, actual.Capacity);
                operation.Commit(actual);
            }
            Assert.Equal(16, owner.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    /// <summary>An independent external mutation executes but prevents a controlled owner from certifying stale facts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalStorageMutationInterruptsOwnerCompletion(bool registered)
    {
        // Arrange an operation candidate around storage that is then changed through an unrelated native call.
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        else fixture.RetireResourceServices();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
                { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 16,
                    Usage = BufferUsage.StaticDraw }).RequireResource();
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                using var binding = OpenGlStateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, owner.BufferId);
                // Act through the native storage API while the managed operation is pending.
                GL.BufferData(BufferTarget.ArrayBuffer, 64, IntPtr.Zero, BufferUsageHint.StaticDraw);
                // Assert the native change interrupted the stale managed candidate.
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(16, BufferUsage.StaticDraw)));
            }
            // Native work was forwarded, even though the interrupted managed candidate cannot commit.
            // Assert the real reader sees the new native capacity after the failed commit.
            Assert.True(new GpuBufferMetadataReader().TryRead(owner.BufferId, out var actual));
            Assert.Equal((nint)64, actual.Capacity);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    /// <summary>Deletion cancels original completion for registered and local resources.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletionCancelsOriginalOperation(bool registered)
    {
        // Arrange an owned allocation with a pending metadata operation.
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        else fixture.RetireResourceServices();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            int id = owner.BufferId;
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                // Act by retiring the original resource before candidate completion.
                RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, id));
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(64, BufferUsage.StaticDraw)));
            }
            // Assert deletion canceled publication and retired the native name.
            Assert.False(GL.IsBuffer(id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    /// <summary>Recovery rejects recursive publication, recovery and operation admission even when the inner failure is caught.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void ReentrantRecoveryCannotPublishStaleCandidate(bool catchFailure, int command)
    {
        // Arrange a reader that attempts recursive publication, refresh, or operation admission.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            var original = new GpuBufferMetadataReader();
            ulong revision = entry.MetadataRevision;
            var reader = new ReentrantReader(original, () =>
            {
                if (command == 0) manager.PublishMetadata(entry, new(128, BufferUsage.DynamicDraw), GpuMetadataChange.Metadata);
                else if (command == 1) manager.RefreshMetadata(entry, GpuMetadataChange.Metadata);
                else using (new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Metadata)) { }
            }, catchFailure);
            entry.Reader = reader;
            ResizeNative(owner, 64);
            // Act through recovery while the configured reader attempts its nested operation.
            Assert.Throws<InvalidOperationException>(() => manager.RefreshMetadata(entry, GpuMetadataChange.Storage));
            // Assert the old canonical candidate stayed withdrawn and no recursive result was committed.
            Assert.Equal(1, reader.Reads);
            Assert.IsType<InvalidOperationException>(reader.InnerFailure);
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            // Canonical history retains the last commit; withdrawn consumers expose no usable snapshot.
            Assert.Equal((nint)16, entry.Metadata.Capacity);
            Assert.False(owner.HasMetadataSnapshot);
            Assert.Equal(0, owner.SizeBytes);
            entry.Reader = original;
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
    #region Local owner projection

    /// <summary>Local selected-image recovery cannot read partial operation storage or retain its previous snapshot.</summary>
    [Fact]
    public void LocalImageAcquisitionWithdrawsBeforeRejectingBusyStorage()
    {
        // Arrange an unregistered owner and a currently valid attachment projection.
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            Assert.Equal(4, image.Width);
            using (var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Storage))
            {
                // Act by attempting image acquisition while the owner still holds its storage operation.
                Assert.Throws<InvalidOperationException>(() => RHIResourceManager.AcquireImageMetadata(image));
                // Assert rejection withdraws the previous local projection before the operation ends.
                Assert.Equal(0, image.Width);
            }
            // Act after the owner recovers the texture facts.
            RHIResourceManager.RecoverWrapperMetadata(texture, new GpuTextureMetadataReader(texture.TextureTarget));
            RHIResourceManager.AcquireImageMetadata(image);
            // Assert only the fresh owner snapshot restores the image projection.
            Assert.Equal(4, image.Width);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    /// <summary>A caught recursive storage publication preserves its effects for the next successful explicit recovery.</summary>
    [Fact]
    public void CaughtReentryPreservesPendingStorageRevision()
    {
        // Arrange a reader that catches nested publication while the outer candidate is being refreshed.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            var native = new GpuBufferMetadataReader();
            ulong storage = entry.StorageRevision;
            entry.Reader = new ReentrantReader(native,
                () => manager.PublishMetadata(entry, new GpuBufferMetadata(64, BufferUsage.StaticDraw), GpuMetadataChange.Storage), true);
            ResizeNative(owner, 64);
            // Act through refresh with the reentrant publication attempted from inside its reader.
            Assert.Throws<InvalidOperationException>(() => manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
            // Assert the caught nested failure left storage revision pending and metadata unavailable.
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(storage, entry.StorageRevision);
            entry.Reader = native;
            // Act through a later explicit refresh using the normal native reader.
            Assert.True(manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
            // Assert the pending storage revision and new capacity commit once.
            Assert.Equal(storage + 1, entry.StorageRevision);
            Assert.Equal(64, owner.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
    #endregion
    #region Private
    /// <summary>Performs controlled native work whose explicit test operation owns publication.</summary>
    private static void ResizeNative(GpuVbo resource, int capacity)
    {
        // Match backend execution: the enclosing operation, not an external observer,
        // certifies this supplied or explicitly recovered candidate.
        OpenGLDriverCalls.NamedBufferData(resource.BufferId, (nint)capacity, IntPtr.Zero,
            OpenTK.Graphics.OpenGL4.BufferUsageHint.StaticDraw);
    }

    /// <summary>Counts recovery while delegating facts to the real native acquisition reader.</summary>
    private sealed class CountingReader : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        internal int Reads;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata metadata)
        {
            Reads++;
            return new GpuBufferMetadataReader().TryRead(id, out metadata);
        }
    }

    /// <summary>Attempts a recursive operation after capturing native facts, optionally catching its failure before returning.</summary>
    private sealed class ReentrantReader(IGpuResourceMetadataReader<GpuBufferMetadata> native, Action callback, bool catchFailure)
        : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        internal int Reads;
        internal Exception? InnerFailure;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata metadata)
        {
            Reads++;
            bool read = native.TryRead(id, out metadata);
            try { callback(); }
            catch (Exception error)
            {
                InnerFailure = error;
                if (!catchFailure) throw;
            }
            return read;
        }
    }

    /// <summary>Receives the same buffer allocation snapshot without taking native deletion ownership.</summary>
    private sealed class Alias(int id) : GpuResource<GpuBufferMetadata>
    {
        internal GpuBufferMetadata Snapshot => metadata;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; } = id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
    }
    #endregion
}
