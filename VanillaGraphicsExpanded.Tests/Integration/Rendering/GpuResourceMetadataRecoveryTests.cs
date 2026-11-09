using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks metadata refresh failure, retired transfer, and interrupted storage publication contracts.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuResourceMetadataRecoveryTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Controlled creation and reader fallback
    /// <summary>Controlled allocation publishes its supplied capacity without consulting an installed failed reader.</summary>
    [Fact]
    public void ControlledAllocationBypassesUnavailableReader()
    {
        // Arrange: create published storage and retain its reader so the test can force reader failure.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(buffer));
            var originalReader = entry.Reader;
            try
            {
                var reader = new UnreadableBuffer();
                entry.Reader = reader;

                // Act: allocate new storage through the controlled wrapper path.
                buffer.Allocate(128);

                // Assert: the operation publishes supplied facts without consulting the failed reader.
                Assert.Equal(0, reader.Reads);
                using var binding = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(buffer.Role), buffer.BufferId);
                GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out int actualCapacity);
                Assert.Equal(128, actualCapacity);
                Assert.Equal(128, buffer.SizeBytes);
                Assert.Equal((nint)128, entry.Metadata.Capacity);
                Assert.True(entry.MetadataAvailable);
                Assert.True(buffer.IsValid);
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
            finally { entry.Reader = originalReader; }
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
    #region Ownership transfer and retirement
    /// <summary>Retired original associations cannot transfer stale native names through either public API.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletedOwnerRejectsDetachedId(bool releaseHandle)
    {
        // Arrange: capture a real buffer owner and its native identity.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(16);
            int oldId = buffer.BufferId;
            // Act: retire the native resource before attempting either ownership-transfer API.
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, oldId));

            // Assert: the stale wrapper cannot recover or transfer its retired identity.
            Assert.False(buffer.IsValid);
            Assert.False(buffer.IsDisposed);
            Assert.False(GL.IsBuffer(oldId));
            Assert.Throws<InvalidOperationException>(() => { if (releaseHandle) buffer.ReleaseHandle(); else buffer.Detach(); });
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Pending retirement, context retirement, and replacement entries cannot make an old owner transferable.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void UnavailableOriginalAssociationRejectsTransfer(bool releaseHandle, int retirement)
    {
        // Arrange: create a native allocation while retaining its original canonical registration.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int native = 0;
        bool contextRetired = false;
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(16);
            native = buffer.BufferId;
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var original = manager.GetResourceRegistration(buffer)!;
            // Act: withdraw the association using one of the pending, retired-context, or same-name replacement paths.
            if (retirement == 0) Assert.True(manager.RetireResource(original));
            else if (retirement == 1)
            {
                RenderContextRegistry.Retire(fixture);
                contextRetired = true;
            }
            else
            {
                // Simulate same-name replacement deterministically without requiring the driver to recycle names.
                manager.ObserveDeletion(GpuResourceKind.Buffer, native);
                manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, native);
            }
            // Assert: neither transfer API can adopt the stale native identity.
            Assert.Throws<InvalidOperationException>(() => { if (releaseHandle) buffer.ReleaseHandle(); else buffer.Detach(); });
            Assert.Equal((nint)native, buffer.ResourceId);
            Assert.False(buffer.IsDisposed);
            Assert.True(GL.IsBuffer(native));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (contextRetired) fixture.RestoreContextRegistration();
            if (native != 0 && GL.IsBuffer(native)) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, native));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Repeated local retirement preserves the transferred allocation and its non-owning alias.</summary>
    [Fact]
    public void TransferredOwnerRemainsIdempotentWhileAliasRemainsLive()
    {
        // Arrange: create an owner and borrower that share the texture's registration.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int native = 0;
        try
        {
            using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var alias = new BorrowedTexture(owner.TextureId);
            // Act: transfer the native name from its owner and repeat disposal-related operations.
            native = checked((int)owner.Detach());
            {
                owner.Dispose();
                // Assert: repeated owner operations do not retire the transferred resource or alias.
                Assert.Equal((nint)0, owner.Detach());
                Assert.Equal((nint)0, owner.ReleaseHandle());
                Assert.True(alias.IsValid);
                Assert.True(GL.IsTexture(native));
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
        }
        finally
        {
            if (native != 0 && GL.IsTexture(native)) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, native));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion
    #region Interrupted operation recovery
    /// <summary>Recovery of an interrupted storage command advances the pending storage revision.</summary>
    [Fact]
    public void IncompleteStorageOperationPreservesStorageRevisionOnRecovery()
    {
        // Arrange: retain the original storage revision before beginning a controlled native replacement.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(texture));
            ulong previousStorageRevision = entry.StorageRevision;
            // Act: execute a real accepted image replacement but abandon its metadata operation.
            using (OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)texture).Target), 0, texture.TextureId))
            using (var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Storage))
            {
                new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current).DefineTextureImages(texture.TextureId, new GpuTextureInitialization { Target = TextureDimension.Two, AllocationMode = GpuTextureAllocationMode.Mutable, InternalFormat = TextureFormat.Rgba8, Width = 12, Height = 6, Levels = 1 }.Resolve(), operation);
                // Abandon the operation after a real accepted image replacement.
            }
            // Assert: consumers cannot use stale facts until explicit native metadata recovery succeeds.
            Assert.False(entry.MetadataAvailable);
            Assert.False(texture.HasMetadataSnapshot);
            Assert.Equal(0, texture.Width);
            var region = new RHITextureRegion(new(), 0, 0, 0, 1, 1, 1);
            var encoding = new RHIPixelEncoding(PixelChannelLayout.Rgba, PixelComponentType.UnsignedByte);
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.ClearTexture(texture, region, encoding, new byte[4]));
            Assert.Same(entry, manager.RecoverResource(GpuResourceKind.Texture, texture.TextureId,
                GpuTextureMetadataReader.Instance));
            Assert.True(texture.HasMetadataSnapshot);
            RHICommandContext.Current.ClearTexture(texture, region, encoding, new byte[4]);
            Assert.Equal(12, texture.Width);
            Assert.Equal(6, texture.Height);
            Assert.True(entry.StorageRevision > previousStorageRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>A real abandoned allocation withdraws buffer snapshots before consumers, until explicit recovery.</summary>
    [Fact]
    public void InterruptedBufferStorageCannotBeConsumedBeforeRecovery()
    {
        // Arrange: retain an allocated buffer and start a storage operation that will not be committed.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var buffer = context.CreateBuffer(new RHIBufferDescription
            { Role = BufferRole.CopySource, Mode = ResourceStorageMode.Mutable, Capacity = 16, Usage = BufferUsage.StreamRead }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(buffer));
            // Act: write native storage and leave the metadata operation incomplete.
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(buffer, GpuMetadataChange.Storage))
                OpenGLDriverCalls.NamedBufferData(buffer.BufferId, 8, 0, OpenTK.Graphics.OpenGL4.BufferUsageHint.StreamRead);

            // Assert: no consumer may act on the withdrawn snapshot before it is re-read.
            Assert.False(entry.IsUsable);
            Assert.False(buffer.HasMetadataSnapshot);
            Assert.Throws<InvalidOperationException>(() => context.ClearBuffer(buffer, 0, 4));
            Assert.Throws<InvalidOperationException>(() => context.MapBuffer(buffer, 0, 4, BufferMapAccess.Read));
            Assert.Same(entry, manager.RecoverResource(GpuResourceKind.Buffer, buffer.BufferId, new GpuBufferMetadataReader()));
            Assert.True(buffer.HasMetadataSnapshot);
            context.ClearBuffer(buffer, 0, 4);
            Assert.Equal(8, buffer.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Construction failure preserves its original exception and retires the unreturned native allocation.</summary>
    [Fact]
    public void FailedOwningConstructionRemovesNativeAllocationAndRegistration()
    {
        // Arrange: register a real buffer and inject construction failure after registry recovery fails.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int buffer = GL.GenBuffer();
        try
        {
            using (OpenGlStateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, buffer))
                GL.BufferData(BufferTarget.ArrayBuffer, 32, IntPtr.Zero, BufferUsageHint.StaticDraw);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = manager.AcquireResource(GpuResourceKind.Buffer, buffer, new GpuBufferMetadataReader())!;
            var failure = new InvalidOperationException("Injected owning construction failure.");

            // Act: fail construction after registration so no owner can be returned to the caller.
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                GpuResource.CompleteOwnedCreation<GpuResource>(GpuResourceKind.Buffer, buffer, () =>
                {
                    // Simulate acquisition failure after registration, before an owner can be returned.
                    Assert.Null(manager.RecoverResource(GpuResourceKind.Buffer, buffer, new UnreadableBuffer()));
                    throw failure;
                })));
            // Assert: the retirement drain removes the failed allocation and its registration.
            manager.OnRenderFrame(0f, RHIResourceManager.Stage);
            Assert.False(GL.IsBuffer(buffer));
            Assert.Null(manager.FindResource(GpuResourceKind.Buffer, buffer));
            Assert.Equal(GpuResourceRegistrationState.Deleted, entry.State);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (GL.IsBuffer(buffer)) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, buffer));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #endregion

    #region Private
    /// <summary>Models a registered allocation whose authoritative native metadata query fails.</summary>
    private sealed class UnreadableBuffer : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public bool IsResource(nint id) => true;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata metadata) { Reads++; metadata = default; return false; }
        #endregion

        #region Internal API
        /// <summary>Gets the number of native metadata reads requested by the manager.</summary>
        internal int Reads;
        #endregion
    }
    #endregion
}
