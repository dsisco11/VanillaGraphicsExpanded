using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks metadata refresh failure, retired transfer, and interrupted storage publication contracts.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuResourceMetadataRecoveryTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Controlled allocation publishes its supplied capacity without consulting an installed failed reader.</summary>
    [Fact]
    public void ControlledAllocationBypassesUnavailableReader()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(buffer));
            var originalReader = entry.Reader;
            try
            {
                var reader = new UnreadableBuffer();
                entry.Reader = reader;
                buffer.Allocate(128);
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

    /// <summary>Retired original associations cannot transfer stale native names through either public API.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletedOwnerRejectsDetachedId(bool releaseHandle)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(16);
            int oldId = buffer.BufferId;
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, oldId));
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
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int native = 0;
        bool contextRetired = false;
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(16);
            native = buffer.BufferId;
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var original = manager.GetResourceRegistration(buffer)!;
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
            Assert.Throws<InvalidOperationException>(() => { if (releaseHandle) buffer.ReleaseHandle(); else buffer.Detach(); });
            Assert.Equal((nint)native, buffer.ResourceId);
            Assert.False(buffer.IsDisposed);
            Assert.True(GL.IsBuffer(native));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (contextRetired) RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            if (native != 0 && GL.IsBuffer(native)) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, native));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Repeated local retirement cannot consult alias guards after an owner has successfully transferred its handle.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransferredOwnerRemainsIdempotentWhileAliasRetainsGuard(bool pass)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int native = 0;
        try
        {
            using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var alias = new BorrowedTexture(owner.TextureId);
            native = checked((int)owner.Detach());
            if (pass) alias.RetainPassReference();
            else alias.RetainLifetimeReference();
            try
            {
                owner.Dispose();
                Assert.Equal((nint)0, owner.Detach());
                Assert.Equal((nint)0, owner.ReleaseHandle());
                Assert.True(alias.IsValid);
                Assert.True(GL.IsTexture(native));
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
            finally
            {
                if (pass) alias.ReleasePassReference();
                else alias.ReleaseLifetimeReference();
            }
        }
        finally
        {
            if (native != 0 && GL.IsTexture(native)) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, native));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Recovery of an interrupted storage command advances the pending storage revision.</summary>
    [Fact]
    public void IncompleteStorageOperationPreservesStorageRevisionOnRecovery()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(texture));
            ulong previousStorageRevision = entry.StorageRevision;
            using (OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)texture).Target), 0, texture.TextureId))
            using (var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Storage))
            {
                new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current).DefineTextureImages(texture.TextureId, new GpuTextureInitialization { Target = TextureDimension.Two, AllocationMode = GpuTextureAllocationMode.Mutable, InternalFormat = TextureFormat.Rgba8, Width = 12, Height = 6, Levels = 1 }.Resolve(), operation);
                // Abandon the operation after a real accepted image replacement.
            }
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(8, texture.Width);
            Assert.Same(entry, manager.RecoverResource(GpuResourceKind.Texture, texture.TextureId,
                GpuTextureMetadataReader.Instance));
            Assert.Equal(12, texture.Width);
            Assert.Equal(6, texture.Height);
            Assert.True(entry.StorageRevision > previousStorageRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Construction failure preserves its original exception and retires the unreturned native allocation.</summary>
    [Fact]
    public void FailedOwningConstructionRemovesNativeAllocationAndRegistration()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int buffer = GL.GenBuffer();
        try
        {
            using (OpenGlStateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, buffer))
                GL.BufferData(BufferTarget.ArrayBuffer, 32, IntPtr.Zero, BufferUsageHint.StaticDraw);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = manager.AcquireResource(GpuResourceKind.Buffer, buffer, new GpuBufferMetadataReader())!;
            var failure = new InvalidOperationException("Injected owning construction failure.");
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                GpuResource.CompleteOwnedCreation<GpuResource>(GpuResourceKind.Buffer, buffer, () =>
                {
                    // Simulate acquisition failure after registration, before an owner can be returned.
                    Assert.Null(manager.RecoverResource(GpuResourceKind.Buffer, buffer, new UnreadableBuffer()));
                    throw failure;
                })));
            manager.OnRenderFrame(0f, GpuResourceManager.Stage);
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

    #region Private
    /// <summary>Models a registered allocation whose authoritative native metadata query fails.</summary>
    private sealed class UnreadableBuffer : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        internal int Reads;
        /// <inheritdoc />
        public bool IsResource(nint id) => true;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata metadata) { Reads++; metadata = default; return false; }
    }
    #endregion
}
