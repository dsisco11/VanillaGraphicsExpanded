using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks explicitly composed operations, original identity and canonical delivery.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuMetadataOperationFoundationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Explicit participation publishes once and registered aliases receive the same canonical result.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitOperationPublishesOnce(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            using var alias = new Alias(owner.BufferId);
            var manager = GpuResourceManagerSystem.CurrentManager;
            var entry = manager?.GetResourceRegistration(owner) as GpuResourceMetadataEntry<GpuBufferMetadata>;
            if (entry is not null) manager!.AssociateResource(entry, alias);
            ulong revision = entry?.MetadataRevision ?? 0, storage = entry?.StorageRevision ?? 0;
            using (var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                operation.RequireParticipation(owner, GpuMetadataChange.Metadata);
                ResizeNative(owner, 64);
                Assert.Equal(registered ? 16 : 0, owner.SizeBytes);
                if (entry is not null) Assert.False(entry.MetadataAvailable);
                operation.Commit(new(64, BufferUsageHint.StaticDraw));
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(128, BufferUsageHint.DynamicDraw)));
                Assert.Equal(64, owner.SizeBytes);
                if (entry is not null) Assert.Equal((nint)64, alias.Snapshot.Capacity);
            }
            if (entry is not null)
            {
                Assert.True(entry.MetadataAvailable);
                Assert.Equal(revision + 1, entry.MetadataRevision);
                Assert.Equal(storage + 1, entry.StorageRevision);
            }
            StateCache.Current.RequireResourceOperationAvailable(GpuResourceKind.Buffer, owner.BufferId);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Disposal never invokes a reader; recovery is a later explicit owner decision.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedOperationDoesNotRecoverOnDispose(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            var manager = GpuResourceManagerSystem.CurrentManager;
            var entry = manager?.GetResourceRegistration(owner) as GpuResourceMetadataEntry<GpuBufferMetadata>;
            var reader = new CountingReader();
            if (entry is not null) entry.Reader = reader;
            ulong storage = entry?.StorageRevision ?? 0;
            using (var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Metadata))
            {
                operation.RequireParticipation(owner, GpuMetadataChange.Storage);
                ResizeNative(owner, 64);
                try { throw new InvalidOperationException("Participating dispatch failed."); }
                catch (InvalidOperationException) { operation.Interrupt(); }
                Assert.Throws<InvalidOperationException>(() => owner.Allocate(128));
                using var alias = new Alias(owner.BufferId);
                Assert.Throws<InvalidOperationException>(() => new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(alias, GpuMetadataChange.Metadata));
                Assert.True(new GpuBufferMetadataReader().TryRead(owner.BufferId, out var unchanged));
                Assert.Equal((nint)64, unchanged.Capacity);
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(64, BufferUsageHint.StaticDraw)));
            }
            Assert.Equal(0, reader.Reads);
            if (entry is not null) Assert.False(entry.MetadataAvailable);
            else Assert.Equal(0, owner.SizeBytes);
            GpuResourceManager.RecoverWrapperMetadata(owner, reader);
            Assert.Equal(1, reader.Reads);
            Assert.Equal(64, owner.SizeBytes);
            if (entry is not null) Assert.Equal(storage + 1, entry.StorageRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A rejected alias operation cannot dispatch or join; an independent resource remains usable.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedAliasMutationIsRejectedBeforeDispatch(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            using var alias = new Alias(owner.BufferId);
            using var independent = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); independent.Allocate(8);
            using (var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Metadata))
            {
                Assert.Throws<InvalidOperationException>(() => new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(alias, GpuMetadataChange.Storage));
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
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A successful explicit reader runs once; disposal performs no additional recovery.</summary>
    [Fact]
    public void OwnerChoosesRecoveryAndCommitsItsResult()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            var reader = new CountingReader();
            using (var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                ResizeNative(owner, 64);
                Assert.True(reader.TryRead(owner.BufferId, out var candidate));
                operation.Commit(candidate);
            }
            Assert.Equal(1, reader.Reads);
            Assert.Equal(64, owner.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Deletion cancels original completion for registered and local resources.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletionCancelsOriginalOperation(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            int id = owner.BufferId;
            using (var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                RHICommandContext.Current.OpenGL.DeleteBuffer(id);
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(64, BufferUsageHint.StaticDraw)));
            }
            Assert.False(GL.IsBuffer(id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A newly registered same-name resource cannot receive the old operation's candidate.</summary>
    [Fact]
    public void ReplacementEntryCannotReceiveOldCandidate()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int id = 0;
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16); id = owner.BufferId;
            var manager = GpuResourceManagerSystem.CurrentManager!;
            using (var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                manager.ObserveDeletion(GpuResourceKind.Buffer, id);
                var next = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, id);
                manager.PublishMetadata(next, new(16, BufferUsageHint.StaticDraw), GpuMetadataChange.None);
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(64, BufferUsageHint.StaticDraw)));
                Assert.True(next.MetadataAvailable);
                Assert.Equal((nint)16, next.Metadata.Capacity);
                Assert.Equal((ulong)1, next.MetadataRevision);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (id != 0 && GL.IsBuffer(id)) RHICommandContext.Current.OpenGL.DeleteBuffer(id);
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
        }
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
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            var original = new GpuBufferMetadataReader();
            ulong revision = entry.MetadataRevision;
            var reader = new ReentrantReader(original, () =>
            {
                if (command == 0) manager.PublishMetadata(entry, new(128, BufferUsageHint.DynamicDraw), GpuMetadataChange.Metadata);
                else if (command == 1) manager.RefreshMetadata(entry, GpuMetadataChange.Metadata);
                else using (new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Metadata)) { }
            }, catchFailure);
            entry.Reader = reader;
            ResizeNative(owner, 64);
            Assert.Throws<InvalidOperationException>(() => manager.RefreshMetadata(entry, GpuMetadataChange.Storage));
            Assert.Equal(1, reader.Reads);
            Assert.IsType<InvalidOperationException>(reader.InnerFailure);
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(16, owner.SizeBytes);
            entry.Reader = original;
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A failed explicitly participating local storage operation invalidates existing view certificates.</summary>
    [Fact]
    public void LocalInterruptedStorageRetiresTextureViewCertificate()
    {
        fixture.EnsureContextValid();
        try
        {
            using var source = DynamicTexture3D.Create(4, 4, 2, PixelInternalFormat.Rgba8, immutableStorage: true);
            using var view = GpuTextureView.CreateFromTexture(source, TextureTarget.Texture2DArray, PixelInternalFormat.Rgba8, 0, 1, 0, 2);
            var certificate = source.UntrackedStorageIdentity;
            using (var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(source, GpuMetadataChange.Metadata))
            {
                operation.RequireParticipation(source, GpuMetadataChange.Storage);
                operation.Interrupt();
                Assert.Throws<InvalidOperationException>(() => operation.Commit(default));
            }
            Assert.NotSame(certificate, source.UntrackedStorageIdentity);
            Assert.False(view.IsValid);
            Assert.Equal(0, source.Width);
            Assert.True(GL.IsTexture(source.TextureId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Local selected-image recovery cannot read partial operation storage or retain its previous snapshot.</summary>
    [Fact]
    public void LocalImageAcquisitionWithdrawsBeforeRejectingBusyStorage()
    {
        fixture.EnsureContextValid();
        try
        {
            using var texture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            Assert.Equal(4, image.Width);
            using (var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Storage))
            {
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.AcquireImageMetadata(image));
                Assert.Equal(0, image.Width);
            }
            GpuResourceManager.RecoverWrapperMetadata(texture, new GpuTextureMetadataReader(texture.TextureTarget));
            GpuResourceManager.AcquireImageMetadata(image);
            Assert.Equal(4, image.Width);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A caught attachment validation failure still interrupts the explicitly supplied framebuffer operation.</summary>
    [Fact]
    public void CaughtAttachmentValidationFailurePreventsCommit()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(manager.GetResourceRegistration(framebuffer));
            var candidate = entry.Metadata;
            ulong revision = entry.MetadataRevision;
            using (var operation = new StateCache.FramebufferOperation(framebuffer))
            {
                Assert.Throws<ArgumentException>(() => image.AttachTo(framebuffer, FramebufferAttachment.DepthAttachment, operation));
                Assert.Throws<InvalidOperationException>(() => operation.Commit());
            }
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A caught recursive storage publication preserves its effects for the next successful explicit recovery.</summary>
    [Fact]
    public void CaughtReentryPreservesPendingStorageRevision()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            var native = new GpuBufferMetadataReader();
            ulong storage = entry.StorageRevision;
            entry.Reader = new ReentrantReader(native,
                () => manager.PublishMetadata(entry, new GpuBufferMetadata(64, BufferUsageHint.StaticDraw), GpuMetadataChange.Storage), true);
            ResizeNative(owner, 64);
            Assert.Throws<InvalidOperationException>(() => manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(storage, entry.StorageRevision);
            entry.Reader = native;
            Assert.True(manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
            Assert.Equal(storage + 1, entry.StorageRevision);
            Assert.Equal(64, owner.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    #endregion

    #region Private
    /// <summary>Changes actual storage for tests whose later publication candidate is already established.</summary>
    private static void ResizeNative(GpuVbo resource, int capacity)
    {
        using var binding = resource.BindScope();
        GL.BufferData(BufferTarget.ArrayBuffer, capacity, IntPtr.Zero, BufferUsageHint.StaticDraw);
    }

    /// <summary>Counts recovery while delegating facts to the real native acquisition reader.</summary>
    private sealed class CountingReader : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        internal int Reads;
        /// <inheritdoc />
        public bool IsResource(nint id) => new GpuBufferMetadataReader().IsResource(id);
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
        public bool IsResource(nint id) => native.IsResource(id);
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
