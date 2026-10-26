using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies controlled buffer and image initialization publishes supplied facts without descriptor recovery.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuBufferImageCreationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>External storage remains unavailable throughout dispatch and only a later explicit acquisition reads the result.</summary>
    [Fact]
    public void ExternalBufferStorageDefersAcquisitionUntilAfterDispatch()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(32);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(buffer));
            var reject = new RejectReader<GpuBufferMetadata>();
            entry.Reader = reject;
            var operation = StateCache.InvalidateNativeBufferStorage(buffer.BufferId, true);
            try
            {
                Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, buffer.BufferId, reject));
                Assert.Equal(0, reject.Calls);
                Assert.False(entry.MetadataAvailable);
                GL.NamedBufferData(buffer.BufferId, 64, IntPtr.Zero, BufferUsageHint.StreamDraw);
            }
            finally { operation.Complete(); }
            Assert.Equal(0, reject.Calls);
            Assert.False(entry.MetadataAvailable);
            Assert.NotNull(manager.AcquireResource(GpuResourceKind.Buffer, buffer.BufferId, new GpuBufferMetadataReader()));
            Assert.Equal(64, buffer.SizeBytes);
            Assert.Equal(BufferUsageHint.StreamDraw, buffer.Usage);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>The approved renderbuffer sample fallback publishes the native sample result without changing requested dimensions or format.</summary>
    [Fact]
    public void RenderbufferFallbackPublishesActualSamples()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var original = GpuSupport.Graphics;
        try
        {
            AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, original with { SupportsInternalFormatQuery2 = false });
            using var renderbuffer = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 8, 4, 3);
            Assert.Equal(8, renderbuffer.Width);
            Assert.Equal(4, renderbuffer.Height);
            Assert.Equal(RenderbufferStorage.Rgba8, renderbuffer.Storage);
            using var binding = renderbuffer.BindScope();
            GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferSamples, out int samples);
            Assert.True(samples >= 3);
            Assert.Equal(samples, renderbuffer.Samples);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, original);
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Concrete family backends retire their native object if identity handoff rejects initialization.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedFamilyHandoffRetiresNativeObject(bool registered, bool renderbuffer)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            int native;
            GpuResourceHandle handle = default;
            if (renderbuffer)
            {
                using var recipient = new FailedRecipient<GpuRenderbufferMetadata>(GpuResourceKind.Renderbuffer);
                var request = new GpuRenderbufferInitialization { Format = RenderbufferStorage.Rgba8, Width = 4, Height = 4 };
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateRenderbuffer(request, recipient, out handle));
                native = recipient.Attempted;
                Assert.Equal((nint)0, recipient.ResourceId);
            }
            else
            {
                using var recipient = new FailedRecipient<GpuBufferMetadata>(GpuResourceKind.Buffer);
                var request = new GpuBufferInitialization { Mode = GpuBufferStorageMode.Mutable, Capacity = 64, Usage = BufferUsageHint.StaticDraw };
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateBuffer(request, recipient, out handle));
                native = recipient.Attempted;
                Assert.Equal((nint)0, recipient.ResourceId);
            }
            Assert.NotEqual(0, native);
            Assert.Equal(default, handle);
            GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.False(renderbuffer ? GL.IsRenderbuffer(native) : GL.IsBuffer(native));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Immutable storage requires explicit flags and retains its native storage behavior through core named dispatch.</summary>
    [Fact]
    public void ImmutableBufferCreationRequiresFlagsAndPublishesCapacity()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer);
            var request = new GpuBufferInitialization { Mode = GpuBufferStorageMode.Immutable, Capacity = 64 };
            Assert.Throws<ArgumentException>(() => GpuResourceManager.CreateBuffer(request, recipient, out _));
            Assert.Equal((nint)0, recipient.ResourceId);
            request.StorageFlags = 0;
            var result = GpuResourceManager.CreateBuffer(request, recipient, out var handle);
            Assert.Equal(new GpuBufferMetadata(64, BufferUsageHint.DynamicDraw, true), result);
            using var binding = StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, (int)handle.Id);
            GL.GetBufferParameter(BufferTarget.ArrayBuffer, (BufferParameterName)All.BufferImmutableStorage, out int immutable);
            GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out int size);
            Assert.Equal(1, immutable);
            Assert.Equal(64, size);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Creation exposes complete facts in both manager and context-local paths and preserves bindings.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BufferCreationPublishesFactsAndPreservesBindings(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var sentinel = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
            using var binding = StateCache.Current.BindBufferScope(BufferTarget.CopyWriteBuffer, sentinel.BufferId);
            using var recipient = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer);
            var request = new GpuBufferInitialization
                { Mode = GpuBufferStorageMode.Mutable, Capacity = 48, Usage = BufferUsageHint.StreamDraw };
            var result = GpuResourceManager.CreateBuffer(request, recipient, out var handle);
            Assert.Equal(new GpuBufferMetadata(48, BufferUsageHint.StreamDraw), result);
            Assert.Equal(result.Value, recipient.Snapshot);
            Assert.Equal(handle.Id, recipient.ResourceId);
            Assert.Equal(sentinel.BufferId, GL.GetInteger((GetPName)All.CopyWriteBufferBinding));
            if (registered)
            {
                var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(
                    GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(recipient));
                Assert.Equal(result.Value, entry.Metadata);
                Assert.Null(entry.Reader);
            }
            using var verify = StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, checked((int)handle.Id));
            GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out int capacity);
            Assert.Equal(48, capacity);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Assigned zero capacity is valid mutable storage, whereas an omitted capacity fails before identity installation.</summary>
    [Fact]
    public void BufferCreationDistinguishesZeroFromOmittedCapacity()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer);
            var request = new GpuBufferInitialization { Mode = GpuBufferStorageMode.Mutable, Usage = BufferUsageHint.StaticDraw };
            Assert.Throws<ArgumentException>(() => GpuResourceManager.CreateBuffer(request, recipient, out _));
            Assert.Equal((nint)0, recipient.ResourceId);
            request.Capacity = 0;
            Assert.Equal(new GpuBufferMetadata(0, BufferUsageHint.StaticDraw),
                GpuResourceManager.CreateBuffer(request, recipient, out _));
            Assert.True(recipient.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Replacement of equal-sized storage still advances its identity and delivers aliases without invoking a reader.</summary>
    [Fact]
    public void ControlledBufferReplacementPublishesAliasesWithoutRecovery()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
            owner.Allocate(32);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            using var alias = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer, owner.BufferId, false);
            manager.AssociateResource(entry, alias);
            var reader = new RejectReader<GpuBufferMetadata>();
            entry.Reader = reader;
            ulong storage = entry.StorageRevision;
            ulong revision = entry.MetadataRevision;
            owner.Allocate(32);
            Assert.Equal(storage + 1, entry.StorageRevision);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal((nint)32, alias.Snapshot.Capacity);
            Assert.Equal(entry.Metadata, alias.Snapshot);
            Assert.Equal(0, reader.Calls);
            Assert.True(manager.IsResourceMetadataAvailable(owner));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Renderbuffer creation publishes dimensions and format without native descriptor acquisition.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RenderbufferCreationPublishesCompleteFacts(bool registered, bool dsa)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities = new DirectStateAccessCapabilityScope(dsa);
            using var sentinel = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 2, 2);
            using var binding = StateCache.Current.BindRenderbufferScope(sentinel.RenderbufferId);
            using var recipient = new Recipient<GpuRenderbufferMetadata>(GpuResourceKind.Renderbuffer);
            var request = new GpuRenderbufferInitialization { Format = RenderbufferStorage.Rgba8, Width = 8, Height = 4 };
            var result = GpuResourceManager.CreateRenderbuffer(request, recipient, out var handle);
            Assert.Equal(new GpuRenderbufferMetadata(8, 4, PixelInternalFormat.Rgba8, 0), result);
            Assert.Equal(sentinel.RenderbufferId, GL.GetInteger(GetPName.RenderbufferBinding));
            Assert.Equal(result.Value, recipient.Snapshot);
            Assert.Equal(handle.Id, recipient.ResourceId);
            if (registered)
            {
                var entry = Assert.IsType<GpuResourceMetadataEntry<GpuRenderbufferMetadata>>(
                    GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(recipient));
                Assert.Null(entry.Reader);
                Assert.Equal(result.Value, entry.Metadata);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Renderbuffer replacement delivers aliases directly even when the optional native reader cannot recover.</summary>
    [Fact]
    public void RenderbufferReplacementPublishesAliasesWithoutRecovery()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 8, 4);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuRenderbufferMetadata>>(manager.GetResourceRegistration(owner));
            using var alias = new Recipient<GpuRenderbufferMetadata>(GpuResourceKind.Renderbuffer, owner.RenderbufferId, false);
            manager.AssociateResource(entry, alias);
            var reader = new RejectReader<GpuRenderbufferMetadata>();
            entry.Reader = reader;
            ulong storage = entry.StorageRevision;
            owner.AllocateStorage(RenderbufferStorage.Rgba8, 16, 8);
            Assert.Equal(storage + 1, entry.StorageRevision);
            Assert.Equal(new GpuRenderbufferMetadata(16, 8, PixelInternalFormat.Rgba8, 0), alias.Snapshot);
            Assert.Equal(0, reader.Calls);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Rejects a nonzero identity once to exercise family cleanup after successful native allocation.</summary>
    private sealed class FailedRecipient<T>(GpuResourceKind kind) : GpuResource<T> where T : struct
    {
        private nint id;
        internal int Attempted;
        /// <inheritdoc />
        public override nint ResourceId
        {
            get => id;
            protected set
            {
                id = value;
                if (value != 0) { Attempted = (int)value; throw new InvalidOperationException("Injected identity handoff failure."); }
            }
        }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => kind;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
    }

    /// <summary>Receives manager delivery with explicit ownership for declarations and borrowed aliases.</summary>
    private sealed class Recipient<T>(GpuResourceKind kind, nint id = 0, bool owns = true) : GpuResource<T> where T : struct
    {
        internal T Snapshot => metadata;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; } = id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => kind;
        /// <inheritdoc />
        protected override bool OwnsResource => owns;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
    }

    /// <summary>Detects any accidental fallback into native descriptor recovery during a controlled mutation.</summary>
    private sealed class RejectReader<T> : IGpuResourceMetadataReader<T> where T : struct
    {
        internal int Calls;
        /// <inheritdoc />
        public bool IsResource(nint id) { Calls++; throw new InvalidOperationException("Unexpected native identity query."); }
        /// <inheritdoc />
        public bool TryRead(nint id, out T metadata) { Calls++; throw new InvalidOperationException("Unexpected native metadata query."); }
    }
    #endregion
}
