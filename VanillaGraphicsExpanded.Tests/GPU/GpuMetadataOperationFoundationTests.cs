using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
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
    /// <summary>A missing original association is a tracking defect, not permission to register or rediscover an operation target.</summary>
    [Fact]
    public void MissingAssociationCannotManufactureOperationIdentity()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
            { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 16, Usage = BufferUsage.StaticDraw }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var original = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            ulong revision = original.MetadataRevision;
            manager.ReplaceWrapperResource(owner);
            try
            {
                Assert.Throws<InvalidOperationException>(() => new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Metadata));
                Assert.Null(manager.GetResourceRegistration(owner));
                Assert.Same(original, manager.FindResource(GpuResourceKind.Buffer, owner.ResourceId));
                Assert.Equal(revision, original.MetadataRevision);
                Assert.True(original.IsUsable);
                Assert.False(original.OperationInProgress);
            }
            finally { manager.AssociateResource(original, owner); }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Explicit participation publishes once and registered aliases receive the same canonical result.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitOperationPublishesOnce(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        else fixture.UseContextLocalResources();
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
                Assert.Equal(registered ? 16 : 0, owner.SizeBytes);
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

    /// <summary>Disposal never invokes a reader; recovery is a later explicit owner decision.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedOperationDoesNotRecoverOnDispose(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        else fixture.UseContextLocalResources();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            var manager = RHIResourceManagerSystem.CurrentManager;
            var entry = manager?.GetResourceRegistration(owner) as GpuResourceMetadataEntry<GpuBufferMetadata>;
            var reader = new CountingReader();
            if (entry is not null) entry.Reader = reader;
            ulong storage = entry?.StorageRevision ?? 0;
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
            Assert.Equal(0, reader.Reads);
            if (entry is not null) Assert.False(entry.MetadataAvailable);
            else Assert.Equal(0, owner.SizeBytes);
            RHIResourceManager.RecoverWrapperMetadata(owner, reader);
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
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        else fixture.UseContextLocalResources();
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
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        else fixture.UseContextLocalResources();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
                { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 16,
                    Usage = BufferUsage.StaticDraw }).RequireResource();
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                using var binding = OpenGlStateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, owner.BufferId);
                GL.BufferData(BufferTarget.ArrayBuffer, 64, IntPtr.Zero, BufferUsageHint.StaticDraw);
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(16, BufferUsage.StaticDraw)));
            }
            // Native work was forwarded, even though the interrupted managed candidate cannot commit.
            Assert.True(new GpuBufferMetadataReader().TryRead(owner.BufferId, out var actual));
            Assert.Equal((nint)64, actual.Capacity);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A successful explicit reader runs once; disposal performs no additional recovery.</summary>
    [Fact]
    public void OwnerChoosesRecoveryAndCommitsItsResult()
    {
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

    /// <summary>Deletion cancels original completion for registered and local resources.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletionCancelsOriginalOperation(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        else fixture.UseContextLocalResources();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            int id = owner.BufferId;
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, id));
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(64, BufferUsage.StaticDraw)));
            }
            Assert.False(GL.IsBuffer(id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A newly registered same-name resource cannot receive the old operation's candidate.</summary>
    [Fact]
    public void ReplacementEntryCannotReceiveOldCandidate()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int id = 0;
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16); id = owner.BufferId;
            var manager = RHIResourceManagerSystem.CurrentManager!;
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                manager.ObserveDeletion(GpuResourceKind.Buffer, id);
                var next = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, id);
                manager.PublishMetadata(next, new(16, BufferUsage.StaticDraw), GpuMetadataChange.None);
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(64, BufferUsage.StaticDraw)));
                Assert.True(next.MetadataAvailable);
                Assert.Equal((nint)16, next.Metadata.Capacity);
                Assert.Equal((ulong)1, next.MetadataRevision);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (id != 0 && GL.IsBuffer(id)) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, id));
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
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
            Assert.Throws<InvalidOperationException>(() => manager.RefreshMetadata(entry, GpuMetadataChange.Storage));
            Assert.Equal(1, reader.Reads);
            Assert.IsType<InvalidOperationException>(reader.InnerFailure);
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(16, owner.SizeBytes);
            entry.Reader = original;
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A failed explicitly participating storage operation makes its view unavailable.</summary>
    [Fact]
    public void LocalInterruptedStorageRetiresTextureViewCertificate()
    {
        fixture.EnsureContextValid();
        fixture.UseContextLocalResources();
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 2, Label = null }).RequireResource();
            var registration = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(source));
            ulong revision = registration.StorageRevision;
            using (var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(source, GpuMetadataChange.Metadata))
            {
                operation.RequireParticipation(source, GpuMetadataChange.Storage);
                operation.Interrupt();
                Assert.Throws<InvalidOperationException>(() => operation.Commit(default));
            }
            Assert.False(registration.IsUsable);
            Assert.Equal(revision, registration.StorageRevision);
            Assert.False(view.IsValid);
            Assert.Equal(0, source.Width);
            Assert.True(GL.IsTexture(source.TextureId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Local selected-image recovery cannot read partial operation storage or retain its previous snapshot.</summary>
    [Fact]
    public void LocalImageAcquisitionWithdrawsBeforeRejectingBusyStorage()
    {
        fixture.EnsureContextValid();
        fixture.UseContextLocalResources();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            Assert.Equal(4, image.Width);
            using (var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Storage))
            {
                Assert.Throws<InvalidOperationException>(() => RHIResourceManager.AcquireImageMetadata(image));
                Assert.Equal(0, image.Width);
            }
            RHIResourceManager.RecoverWrapperMetadata(texture, new GpuTextureMetadataReader(texture.TextureTarget));
            RHIResourceManager.AcquireImageMetadata(image);
            Assert.Equal(4, image.Width);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A caught attachment validation failure still interrupts the explicitly supplied framebuffer operation.</summary>
    [Fact]
    public void CaughtAttachmentValidationFailurePreventsCommit()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(manager.GetResourceRegistration(framebuffer));
            var candidate = entry.Metadata;
            ulong revision = entry.MetadataRevision;
            using (var operation = new OpenGLFramebufferConfiguration.FramebufferOperation(framebuffer))
            {
                Assert.Throws<ArgumentException>(() => VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLFramebufferConfiguration.FramebufferTexture(framebuffer, FramebufferSlot.Depth, image, operation));
                Assert.Throws<InvalidOperationException>(() => operation.Commit());
            }
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A caught recursive storage publication preserves its effects for the next successful explicit recovery.</summary>
    [Fact]
    public void CaughtReentryPreservesPendingStorageRevision()
    {
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
            Assert.Throws<InvalidOperationException>(() => manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(storage, entry.StorageRevision);
            entry.Reader = native;
            Assert.True(manager.RefreshMetadata(entry, GpuMetadataChange.Metadata));
            Assert.Equal(storage + 1, entry.StorageRevision);
            Assert.Equal(64, owner.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

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
