using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks explicitly composed operations, original identity and canonical delivery.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuMetadataOperationFoundationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A missing original association is a tracking defect, not permission to register or rediscover an operation target.</summary>
    [Fact]
    public void MissingAssociationCannotManufactureOperationIdentity()
    {
        // Arrange a registered owner whose managed alias is deliberately removed.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
            { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 16, Usage = BufferUsage.StaticDraw }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var original = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            ulong revision = original.MetadataRevision;
            // Act by removing the wrapper association before operation construction.
            manager.ReplaceWrapperResource(owner);
            // Assert the missing association is rejected without replacing the original entry.
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


    /// <summary>A newly registered same-name resource cannot receive the old operation's candidate.</summary>
    [Fact]
    public void ReplacementEntryCannotReceiveOldCandidate()
    {
        // Arrange a pending operation on the original buffer registration.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int id = 0;
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16); id = owner.BufferId;
            var manager = RHIResourceManagerSystem.CurrentManager!;
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                // Act by deleting and re-registering the same numeric identity before the old candidate commits.
                manager.ObserveDeletion(GpuResourceKind.Buffer, id);
                var next = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, id);
                manager.PublishMetadata(next, new(16, BufferUsage.StaticDraw), GpuMetadataChange.None);
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(64, BufferUsage.StaticDraw)));
                // Assert only the replacement entry retains its own initial snapshot.
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


    /// <summary>A failed explicitly participating storage operation makes its view unavailable.</summary>
    [Fact]
    public void LocalInterruptedStorageRetiresTextureViewCertificate()
    {
        // Arrange an unregistered source texture and a dependent view with a valid certificate.
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 2, Label = null }).RequireResource();
            var registration = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(source));
            ulong revision = registration.StorageRevision;
            // Act by interrupting the explicit storage operation before its candidate can commit.
            using (var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(source, GpuMetadataChange.Metadata))
            {
                operation.RequireParticipation(source, GpuMetadataChange.Storage);
                operation.Interrupt();
                Assert.Throws<InvalidOperationException>(() => operation.Commit(default));
            }
            // Assert interruption invalidates the dependent view while native storage remains allocated.
            Assert.False(registration.IsUsable);
            Assert.Equal(revision, registration.StorageRevision);
            Assert.False(view.IsValid);
            Assert.Equal(0, source.Width);
            Assert.True(GL.IsTexture(source.TextureId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    /// <summary>A caught attachment validation failure still interrupts the explicitly supplied framebuffer operation.</summary>
    [Fact]
    public void CaughtAttachmentValidationFailurePreventsCommit()
    {
        // Arrange a registered framebuffer operation with an image invalid for the requested attachment slot.
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
                // Act by catching attachment validation failure before attempting operation completion.
                Assert.Throws<ArgumentException>(() => VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLFramebufferConfiguration.FramebufferTexture(framebuffer, FramebufferSlot.Depth, image, operation));
                Assert.Throws<InvalidOperationException>(() => operation.Commit());
            }
            // Assert the caught native failure still interrupted publication.
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(revision + 1, entry.MetadataRevision);
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
