using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks native metadata publication independently from requested creation policy and content uploads.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuResourceMetadataPublicationTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Buffer storage publication
    /// <summary>Allocation delivers the same measured capacity to an owner, its typed alias and the canonical registry.</summary>
    [Fact]
    public void BufferAllocationPublishesNativeCapacityToEveryAlias()
    {
        // Arrange: register a buffer owner and attach a typed alias to its canonical metadata entry.
        using var lifetime = StartLifetime(true);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        owner.Allocate(64);
        var manager = RHIResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
        using var alias = new BufferAlias(owner.BufferId);
        manager.AssociateResource(entry, alias);
        // Act: replace storage with a new capacity.
        owner.Allocate(192);

        // Assert: owner, alias, registry, and native driver agree on the new capacity.
        Assert.Equal(192, owner.SizeBytes);
        Assert.Equal((nint)owner.SizeBytes, alias.Capacity);
        Assert.Equal(entry.Metadata.Capacity, alias.Capacity);
        Assert.Equal(BufferUsage.DynamicDraw, alias.Usage);
        using var binding = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(owner.Role), owner.BufferId);
        GL.GetBufferParameter(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(owner.Role), BufferParameterName.BufferSize, out int actualCapacity);
        Assert.Equal(actualCapacity, owner.SizeBytes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Updating buffer bytes leaves storage facts and metadata revisions unchanged.</summary>
    [Fact]
    public void ContentUploadDoesNotPublishAnotherMetadataRevision()
    {
        // Arrange: allocate storage and capture its metadata and storage revision counters.
        using var lifetime = StartLifetime(true);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        owner.Allocate(64);
        var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
        ulong metadataRevision = entry.MetadataRevision, storageRevision = entry.StorageRevision;
        // Act: update a bounded byte range without reallocating the buffer.
        owner.UploadSubData(new byte[] { 1, 2, 3, 4 }, 8, 4);

        // Assert: revisions and capacity stay fixed while the requested bytes reach native storage.
        Assert.Equal(metadataRevision, entry.MetadataRevision);
        Assert.Equal(storageRevision, entry.StorageRevision);
        Assert.Equal(64, owner.SizeBytes);
        using var binding = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(owner.Role), owner.BufferId);
        byte[] actual = new byte[4];
        GL.GetBufferSubData(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(owner.Role), (IntPtr)8, (IntPtr)4, actual);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, actual);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An invalid controlled storage request is rejected before dispatch and preserves the published capacity.</summary>
    [Fact]
    public void RejectedBufferAllocationPreservesMeasuredCapacity()
    {
        using var lifetime = StartLifetime(true);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        // Arrange: preserve a valid allocation in both the owner and its alias.
        owner.Allocate(64);
        var manager = RHIResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
        using var alias = new BufferAlias(owner.BufferId);
        manager.AssociateResource(entry, alias);
        try
        {
            using var binding = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(owner.Role), owner.BufferId);
            // Bypass the owning API to verify the OpenGlStateCache command boundary also validates storage inputs.

            // Act: issue an invalid size through the production command boundary.
            Assert.Throws<ArgumentOutOfRangeException>(() => GpuBufferUploader.Allocate(owner, -1, IntPtr.Zero, BufferUsage.StaticDraw));

            // Assert: rejection preserves the measured storage facts and leaves no GL error.
            Assert.Equal(64, owner.SizeBytes);
            Assert.Equal((nint)64, alias.Capacity);
            Assert.Equal((nint)64, entry.Metadata.Capacity);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { while (GL.GetError() != ErrorCode.NoError) { } }
    }

    /// <summary>The named managed-array upload preserves bindings and publishes actual storage to aliases exactly once.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void ManagedArrayUploaderPreservesBindingsAndPublishesStorageOnce(int byteCount)
    {
        // Arrange: bind a sentinel buffer and retain the owner entry's pre-operation revisions.
        using var lifetime = StartLifetime(true);
        Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        using var sentinel = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        owner.Allocate(64);
        var manager = RHIResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
        using var alias = new BufferAlias(owner.BufferId);
        manager.AssociateResource(entry, alias);
        ulong metadataRevision = entry.MetadataRevision, storageRevision = entry.StorageRevision;
        using var sentinelBinding = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(sentinel.Role), sentinel.BufferId);
        byte[] expected = [9, 8, 7, 6];
        // Act: allocate the requested byte count through the managed-array upload overload.
        GpuBufferUploader.Allocate(owner, byteCount, expected, BufferUsage.StreamDraw);

        // Assert: the unrelated binding survives and storage metadata advances once.
        Assert.Equal(sentinel.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
        Assert.Equal(byteCount, owner.SizeBytes);
        Assert.Equal((nint)byteCount, alias.Capacity);
        Assert.Equal(BufferUsage.StreamDraw, alias.Usage);
        Assert.Equal(metadataRevision + 1, entry.MetadataRevision);
        Assert.Equal(storageRevision + 1, entry.StorageRevision);
        using var binding = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(owner.Role), owner.BufferId);
        if (byteCount != 0)
        {
            byte[] actual = new byte[byteCount];
            GL.GetBufferSubData(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(owner.Role), IntPtr.Zero, (IntPtr)actual.Length, actual);
            Assert.Equal(expected, actual);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A rejected named array command preserves the other bound buffer and the measured allocation.</summary>
    [Fact]
    public void RejectedManagedArrayUploadPreservesBindingAndMeasuredStorage()
    {
        // Arrange: retain a valid owner allocation and an unrelated buffer binding.
        using var lifetime = StartLifetime(true);
        Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        using var sentinel = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        owner.Allocate(64);
        var manager = RHIResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
        using var alias = new BufferAlias(owner.BufferId);
        manager.AssociateResource(entry, alias);
        using var sentinelBinding = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(sentinel.Role), sentinel.BufferId);
        try
        {
            // Act: pass an invalid size with a payload through the managed-array uploader.
            Assert.Throws<ArgumentOutOfRangeException>(() => GpuBufferUploader.Allocate(owner, -1, new byte[4], BufferUsage.StreamDraw));

            // Assert: rejection changes neither the caller's binding nor published storage.
            Assert.Equal(sentinel.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
            Assert.Equal(64, owner.SizeBytes);
            Assert.Equal((nint)64, alias.Capacity);
            Assert.Equal((nint)64, entry.Metadata.Capacity);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { while (GL.GetError() != ErrorCode.NoError) { } }
    }

    /// <summary>Requested usage stays separate from measured storage across direct-state and generated-name paths.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestedBufferUsageDoesNotMasqueradeAsAllocatedStorage(bool managed)
    {
        // Arrange: create an empty owner under the selected global metadata-service state.
        using var lifetime = StartLifetime(managed);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        // Direct-state creation realizes an empty native object immediately. The legacy
        // generated-name path has no metadata to expose until its first storage command.
        if (GL.IsBuffer(owner.BufferId))
        {
            using var binding = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(owner.Role), owner.BufferId);
            GL.GetBufferParameter(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(owner.Role), BufferParameterName.BufferUsage, out int actualUsage);
            GL.GetBufferParameter(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(owner.Role), BufferParameterName.BufferSize, out int actualCapacity);
            Assert.Equal(OpenGLBufferValues.Usage((BufferUsageHint)actualUsage), owner.Usage);
            Assert.Equal(actualCapacity, owner.SizeBytes);
        }
        // Act: allocate actual storage so the requested usage becomes an allocation fact.
        owner.Allocate(32);

        // Assert: the resulting wrapper and native capacity reflect the allocated storage.
        Assert.Equal(BufferUsage.DynamicDraw, owner.Usage);
        Assert.Equal(32, owner.SizeBytes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion
    #region Image storage publication
    /// <summary>Texture storage snapshots describe the actual resized image with or without a global lifecycle service.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextureResizePublishesActualImageDimensions(bool managed)
    {
        // Arrange: retain an attachment alias to observe metadata delivery after owner resize.
        using var lifetime = StartLifetime(managed);
        using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(owner);
        // Act: replace the texture image dimensions.
        Assert.True(owner.Resize(12, 6));

        // Assert: owner and attachment report the dimensions stored by the driver.
        Assert.Equal(12, owner.Width);
        Assert.Equal(6, owner.Height);
        Assert.Equal(owner.Width, image.Width);
        Assert.Equal(owner.Height, image.Height);
        Assert.Equal(TextureFormat.Rgba16f, image.InternalFormat);
        GL.GetTextureLevelParameter(owner.TextureId, 0, GetTextureParameter.TextureWidth, out int actualWidth);
        Assert.Equal(actualWidth, owner.Width);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Renderbuffer descriptors and selected attachment images reflect measured native storage.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderbufferCreationPublishesNativeImage(bool managed)
    {
        using var lifetime = StartLifetime(managed);
        // Arrange: select whether resource registration is enabled for this native allocation.

        // Act: create renderbuffer storage and its managed attachment wrapper through production APIs.
        using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 7, Height = 5, Samples = 0, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromRenderbuffer(owner);
        // Assert: both wrappers and the driver report the same renderbuffer image.
        Assert.Equal(7, owner.Width);
        Assert.Equal(5, owner.Height);
        Assert.Equal(owner.Width, image.Width);
        Assert.Equal(owner.Height, image.Height);
        Assert.Equal(TextureFormat.Rgba8, image.InternalFormat);
        using var binding = OpenGlStateCache.Current.BindRenderbufferScope(owner.RenderbufferId);
        GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferWidth, out int actualWidth);
        Assert.Equal(actualWidth, owner.Width);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion
    #region View publication
    /// <summary>View descriptors expose measured native ranges rather than the owning resource's creation policy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ViewsPublishNativeDescriptors(bool managed)
    {
        // Arrange: create the source array image and select a nonzero subrange as a texture view.
        using var lifetime = StartLifetime(managed);
        using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 6), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        // Act: create the selected image view and then a buffer texture over a separate allocation.
        using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 2, LayerCount = 3, Label = null }).RequireResource();
        // Assert: the selected native view range and buffer association are exposed in metadata.
        Assert.Equal(2, view.BaseLayer);
        Assert.Equal(3, view.LayerCount);
        GL.GetTextureParameter(view.TextureId, GetTextureParameter.TextureViewMinLayer, out int actualBaseLayer);
        GL.GetTextureParameter(view.TextureId, GetTextureParameter.TextureViewNumLayers, out int actualLayerCount);
        Assert.Equal(actualBaseLayer, view.BaseLayer);
        Assert.Equal(actualLayerCount, view.LayerCount);
        using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        buffer.Allocate(1024);
        using var bufferView = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
        Assert.Equal(buffer.BufferId, bufferView.BufferId);
        Assert.Equal(TextureFormat.R32f, bufferView.Format);
        GL.GetTextureLevelParameter(bufferView.TextureId, 0, (GetTextureParameter)All.TextureBufferDataStoreBindingExt, out int actualBuffer);
        Assert.Equal(actualBuffer, bufferView.BufferId);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Direct-state buffer-view creation preserves bindings and publishes the native range.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BufferViewCreationPreservesBindingsAndPublishesNativeRange(bool ranged)
    {
        // Arrange: bind a sentinel on multiple units and retain the caller's active texture unit.
        using var lifetime = StartLifetime(true);
        Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        Assert.True(GpuSupport.Graphics.SupportsTextureBufferRange);
        int offset = Math.Max(1, GpuSupport.Graphics.TextureBufferOffsetAlignment);
        const int size = 256;
        using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        buffer.Allocate(checked(offset * 2 + size));
        using var sentinel = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
        var cache = OpenGlStateCache.Current;
        OpenGLImageBindings.BindTexture(cache, TextureTarget.TextureBuffer, 0, sentinel.TextureId);
        OpenGLImageBindings.BindTexture(cache, TextureTarget.TextureBuffer, 3, sentinel.TextureId);
        GL.ActiveTexture(TextureUnit.Texture3);
        cache.InvalidateAll();
#if DEBUG
        long previousBindCount = cache.TextureBindCount;
#endif
        try
        {
            // Act: create the whole-buffer or ranged view using the selected production overload.
            using var view = ranged
                    ? RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = false, Offset = offset, Length = size, Label = null }).RequireResource()
                    : RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
#if DEBUG
            Assert.Equal(previousBindCount, cache.TextureBindCount);
#endif
            // Assert: creation preserves bindings and publishes the actual native range and format.
            Assert.Equal((int)TextureUnit.Texture3, GL.GetInteger(GetPName.ActiveTexture));
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBindingBuffer));
            GL.ActiveTexture(TextureUnit.Texture0);
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBindingBuffer));
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.GetTextureLevelParameter(view.TextureId, 0, (GetTextureParameter)All.TextureBufferDataStoreBindingExt, out int actualBuffer);
            GL.GetTextureLevelParameter(view.TextureId, 0, GetTextureParameter.TextureInternalFormat, out int actualFormat);
            Assert.Equal(actualBuffer, view.BufferId);
            Assert.Equal(buffer.BufferId, actualBuffer);
            Assert.Equal(OpenGLImageValues.Format((PixelInternalFormat)actualFormat), view.Format);
            if (ranged)
            {
                GL.GetTextureLevelParameter(view.TextureId, 0, (GetTextureParameter)All.TextureBufferOffset, out int actualOffset);
                GL.GetTextureLevelParameter(view.TextureId, 0, (GetTextureParameter)All.TextureBufferSize, out int actualSize);
                Assert.Equal((nint)actualOffset, view.OffsetBytes);
                Assert.Equal((nint)actualSize, view.SizeBytes);
                Assert.Equal((nint)offset, view.OffsetBytes);
                Assert.Equal((nint)size, view.SizeBytes);
            }
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(view));
            using var alias = new TextureAlias(view.TextureId);
            manager.AssociateResource(entry, alias);
            Assert.Equal(entry.Metadata.Buffer, alias.Buffer);
            Assert.Equal(new GpuTextureBufferMetadata(buffer.BufferId, TextureFormat.R32f,
                ranged ? offset : 0, ranged ? size : 0, !ranged), alias.Buffer);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.ActiveTexture(TextureUnit.Texture0); cache.InvalidateAll(); }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Starts an isolated optional manager lifetime after removing any fixture-owned service.</summary>
    private Lifetime StartLifetime(bool managed)
    {
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        return new Lifetime(managed);
    }

    /// <summary>Provides a non-owning typed recipient without implementing any metadata acquisition logic.</summary>
    private sealed class BufferAlias : GpuResource<GpuBufferMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
        #endregion

        #region Protected
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        #endregion

        #region Internal API
        /// <summary>Retains only the identity of the existing allocation.</summary>
        internal BufferAlias(int id) { ResourceId = id; }
        /// <summary>Gets the last manager-delivered capacity.</summary>
        internal nint Capacity => metadata.Capacity;
        /// <summary>Gets the last manager-delivered storage usage.</summary>
        internal BufferUsage Usage => metadata.Usage;
        #endregion
    }


    /// <summary>Receives texture metadata through the manager without acquiring native ownership.</summary>
    private sealed class TextureAlias : GpuResource<GpuTextureMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
        #endregion

        #region Protected
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        #endregion

        #region Internal API
        /// <summary>Retains an existing native identity for typed snapshot delivery.</summary>
        internal TextureAlias(int id) { ResourceId = id; }
        /// <summary>Gets the last manager-published buffer association.</summary>
        internal GpuTextureBufferMetadata? Buffer => metadata.Buffer;
        #endregion
    }

    /// <summary>Contains service state so offline coverage cannot inherit a manager from another test.</summary>
    private sealed class Lifetime : IDisposable
    {
        private readonly RHIResourceManager? manager;
        #region Public API
        /// <summary>Drains owning resources before closing registration and clearing cached bindings.</summary>
        public void Dispose()
        {
            manager?.Dispose();
            if (manager is not null) RHIResourceManagerSystem.Shutdown();
            OpenGlStateCache.Current.InvalidateAll();
        }
        #endregion

        #region Internal API
        /// <summary>Optionally installs a service in the current hidden context.</summary>
        internal Lifetime(bool managed)
        {
            Assert.Null(RHIResourceManagerSystem.CurrentManager);
            if (managed) { manager = new RHIResourceManager(); RHIResourceManagerSystem.Initialize(manager); }
        }
        #endregion
    }
    #endregion
}
