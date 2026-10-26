using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks native metadata publication independently from requested creation policy and content uploads.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuResourceMetadataPublicationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Allocation delivers the same measured capacity to an owner, its typed alias and the canonical registry.</summary>
    [Fact]
    public void BufferAllocationPublishesNativeCapacityToEveryAlias()
    {
        using var lifetime = StartLifetime(true);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource();
        owner.Allocate(64);
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
        using var alias = new BufferAlias(owner.BufferId);
        manager.AssociateResource(entry, alias);
        owner.Allocate(192);
        Assert.Equal(192, owner.SizeBytes);
        Assert.Equal((nint)owner.SizeBytes, alias.Capacity);
        Assert.Equal(entry.Metadata.Capacity, alias.Capacity);
        Assert.Equal(BufferUsageHint.DynamicDraw, alias.Usage);
        using var binding = owner.BindScope();
        GL.GetBufferParameter(owner.Target, BufferParameterName.BufferSize, out int actualCapacity);
        Assert.Equal(actualCapacity, owner.SizeBytes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Updating buffer bytes leaves storage facts and metadata revisions unchanged.</summary>
    [Fact]
    public void ContentUploadDoesNotPublishAnotherMetadataRevision()
    {
        using var lifetime = StartLifetime(true);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
        owner.Allocate(64);
        var entry = GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
        ulong metadataRevision = entry.MetadataRevision, storageRevision = entry.StorageRevision;
        owner.UploadSubData(new byte[] { 1, 2, 3, 4 }, 8, 4);
        Assert.Equal(metadataRevision, entry.MetadataRevision);
        Assert.Equal(storageRevision, entry.StorageRevision);
        Assert.Equal(64, owner.SizeBytes);
        using var binding = owner.BindScope();
        byte[] actual = new byte[4];
        GL.GetBufferSubData(owner.Target, (IntPtr)8, (IntPtr)4, actual);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, actual);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An invalid controlled storage request is rejected before dispatch and preserves the published capacity.</summary>
    [Fact]
    public void RejectedBufferAllocationPreservesMeasuredCapacity()
    {
        using var lifetime = StartLifetime(true);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
        owner.Allocate(64);
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
        using var alias = new BufferAlias(owner.BufferId);
        manager.AssociateResource(entry, alias);
        try
        {
            // Bypass the owning API to verify the StateCache command boundary also validates storage inputs.
            using var binding = owner.BindScope();
            Assert.Throws<ArgumentOutOfRangeException>(() => GpuBufferUploader.Allocate(owner, -1, IntPtr.Zero, RHIBufferUsage.StaticDraw));
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
        using var lifetime = StartLifetime(true);
        Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
        using var sentinel = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
        owner.Allocate(64);
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
        using var alias = new BufferAlias(owner.BufferId);
        manager.AssociateResource(entry, alias);
        ulong metadataRevision = entry.MetadataRevision, storageRevision = entry.StorageRevision;
        using var sentinelBinding = sentinel.BindScope();
        byte[] expected = [9, 8, 7, 6];
        GpuBufferUploader.Allocate(owner, byteCount, expected, RHIBufferUsage.StreamDraw);
        Assert.Equal(sentinel.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
        Assert.Equal(byteCount, owner.SizeBytes);
        Assert.Equal((nint)byteCount, alias.Capacity);
        Assert.Equal(BufferUsageHint.StreamDraw, alias.Usage);
        Assert.Equal(metadataRevision + 1, entry.MetadataRevision);
        Assert.Equal(storageRevision + 1, entry.StorageRevision);
        using var binding = owner.BindScope();
        if (byteCount != 0)
        {
            byte[] actual = new byte[byteCount];
            GL.GetBufferSubData(owner.Target, IntPtr.Zero, (IntPtr)actual.Length, actual);
            Assert.Equal(expected, actual);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A rejected named array command preserves the other bound buffer and the measured allocation.</summary>
    [Fact]
    public void RejectedManagedArrayUploadPreservesBindingAndMeasuredStorage()
    {
        using var lifetime = StartLifetime(true);
        Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
        using var sentinel = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
        owner.Allocate(64);
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
        using var alias = new BufferAlias(owner.BufferId);
        manager.AssociateResource(entry, alias);
        using var sentinelBinding = sentinel.BindScope();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GpuBufferUploader.Allocate(owner, -1, new byte[4], RHIBufferUsage.StreamDraw));
            Assert.Equal(sentinel.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
            Assert.Equal(64, owner.SizeBytes);
            Assert.Equal((nint)64, alias.Capacity);
            Assert.Equal((nint)64, entry.Metadata.Capacity);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { while (GL.GetError() != ErrorCode.NoError) { } }
    }

    /// <summary>Requested allocation usage stays separate from the native defaults until storage is allocated.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestedBufferUsageDoesNotMasqueradeAsAllocatedStorage(bool managed)
    {
        using var lifetime = StartLifetime(managed);
        using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource();
        // Direct-state creation realizes an empty native object immediately. The legacy
        // generated-name path has no metadata to expose until its first storage command.
        if (GL.IsBuffer(owner.BufferId))
        {
            using var binding = owner.BindScope();
            GL.GetBufferParameter(owner.Target, BufferParameterName.BufferUsage, out int actualUsage);
            GL.GetBufferParameter(owner.Target, BufferParameterName.BufferSize, out int actualCapacity);
            Assert.Equal((BufferUsageHint)actualUsage, owner.Usage);
            Assert.Equal(actualCapacity, owner.SizeBytes);
        }
        owner.Allocate(32);
        Assert.Equal(BufferUsageHint.DynamicDraw, owner.Usage);
        Assert.Equal(32, owner.SizeBytes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Texture storage snapshots describe the actual resized image with or without a global lifecycle service.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextureResizePublishesActualImageDimensions(bool managed)
    {
        using var lifetime = StartLifetime(managed);
        using var owner = DynamicTexture2D.Create(8, 4, PixelInternalFormat.Rgba16f);
        using var image = GpuFramebufferAttachment.FromTexture(owner);
        Assert.True(owner.Resize(12, 6));
        Assert.Equal(12, owner.Width);
        Assert.Equal(6, owner.Height);
        Assert.Equal(owner.Width, image.Width);
        Assert.Equal(owner.Height, image.Height);
        Assert.Equal(PixelInternalFormat.Rgba16f, image.InternalFormat);
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
        using var owner = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 7, 5);
        using var image = GpuFramebufferAttachment.FromRenderbuffer(owner);
        Assert.Equal(7, owner.Width);
        Assert.Equal(5, owner.Height);
        Assert.Equal(owner.Width, image.Width);
        Assert.Equal(owner.Height, image.Height);
        Assert.Equal(PixelInternalFormat.Rgba8, image.InternalFormat);
        using var binding = StateCache.Current.BindRenderbufferScope(owner.RenderbufferId);
        GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferWidth, out int actualWidth);
        Assert.Equal(actualWidth, owner.Width);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>View descriptors expose measured native ranges rather than the owning resource's creation policy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ViewsPublishNativeDescriptors(bool managed)
    {
        using var lifetime = StartLifetime(managed);
        using var source = DynamicTexture3D.Create(8, 4, 6, PixelInternalFormat.Rgba32f, immutableStorage: true);
        using var view = GpuTextureView.CreateFromTexture(source, TextureTarget.Texture2DArray, PixelInternalFormat.Rgba32f, 0, 1, 2, 3);
        Assert.Equal(2, view.BaseLayer);
        Assert.Equal(3, view.LayerCount);
        GL.GetTextureParameter(view.TextureId, GetTextureParameter.TextureViewMinLayer, out int actualBaseLayer);
        GL.GetTextureParameter(view.TextureId, GetTextureParameter.TextureViewNumLayers, out int actualLayerCount);
        Assert.Equal(actualBaseLayer, view.BaseLayer);
        Assert.Equal(actualLayerCount, view.LayerCount);
        using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
        buffer.Allocate(1024);
        using var bufferView = GpuBufferView.CreateWholeBuffer(buffer.BufferId, SizedInternalFormat.R32f);
        Assert.Equal(buffer.BufferId, bufferView.BufferId);
        Assert.Equal(SizedInternalFormat.R32f, bufferView.Format);
        GL.GetTextureLevelParameter(bufferView.TextureId, 0, (GetTextureParameter)All.TextureBufferDataStoreBindingExt, out int actualBuffer);
        Assert.Equal(actualBuffer, bufferView.BufferId);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Allocating all cube faces and mip images publishes one complete storage snapshot.</summary>
    [Fact]
    public void CubeAllocationPublishesCompleteMipChainOnce()
    {
        using var lifetime = StartLifetime(true);
        using var cube = DynamicTextureCube.Create(8, PixelInternalFormat.Rgba16f, mipLevels: 3);
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(cube));
        Assert.True(entry.MetadataAvailable);
        Assert.Equal((ulong)1, entry.MetadataRevision);
        Assert.Equal((ulong)0, entry.StorageRevision);
        Assert.Equal(18, entry.Metadata.Levels.Length);
        Assert.All(entry.Metadata.Levels, image => Assert.True(image.Allocated));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A failed framebuffer operation cannot expose earlier successful commands as a complete configuration.</summary>
    [Fact]
    public void FailedAttachmentOperationLeavesCanonicalMetadataUnavailable()
    {
        using var lifetime = StartLifetime(true);
        using var originalTexture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using var replacementTexture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using var original = GpuFramebufferAttachment.FromTexture(originalTexture);
        using var replacement = GpuFramebufferAttachment.FromTexture(replacementTexture);
        using var framebuffer = DynamicGpuFramebuffer.Create([original])!;
        var cache = StateCache.Current;
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(manager.GetResourceRegistration(framebuffer));
        ulong revision = entry.MetadataRevision;
        var previous = entry.Metadata.Attachments[FramebufferAttachment.ColorAttachment0];
        using var bindings = cache.BindFramebufferScope();
        framebuffer.Bind();
        try
        {
            using (var operation = new StateCache.FramebufferOperation(framebuffer))
            {
                replacement.AttachTo(framebuffer, FramebufferAttachment.ColorAttachment0, operation);
                Assert.False(entry.MetadataAvailable);
                Assert.Equal(revision, entry.MetadataRevision);
                var invalid = FramebufferAttachment.DepthAttachment;
                Assert.Throws<ArgumentException>(() => replacement.AttachTo(framebuffer, invalid, operation));
            }
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(previous, entry.Metadata.Attachments[FramebufferAttachment.ColorAttachment0]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { while (GL.GetError() != ErrorCode.NoError) { } }
    }

    /// <summary>Named creation and generated-name fallback preserve bindings while publishing actual buffer associations.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BufferViewCreationPreservesBindingsAndPublishesNativeRange(bool generatedName, bool ranged)
    {
        using var lifetime = StartLifetime(true);
        Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        Assert.True(GpuSupport.Graphics.SupportsTextureBufferRange);
        int offset = Math.Max(1, GpuSupport.Graphics.TextureBufferOffsetAlignment);
        const int size = 256;
        using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
        buffer.Allocate(checked(offset * 2 + size));
        using var sentinel = GpuBufferView.CreateWholeBuffer(buffer.BufferId, SizedInternalFormat.R32f);
        var cache = StateCache.Current;
        cache.BindTexture(TextureTarget.TextureBuffer, 0, sentinel.TextureId);
        cache.BindTexture(TextureTarget.TextureBuffer, 3, sentinel.TextureId);
        GL.ActiveTexture(TextureUnit.Texture3);
        cache.InvalidateAll();
#if DEBUG
        long previousBindCount = cache.TextureBindCount;
#endif
        try
        {
            using var capabilities = new DirectStateAccessCapabilityScope(!generatedName);
            using var view = generatedName
                ? new GeneratedBufferView(buffer.BufferId, ranged ? offset : 0, ranged ? size : 0)
                : ranged
                    ? GpuBufferView.CreateRange(buffer.BufferId, SizedInternalFormat.R32f, offset, size)
                    : GpuBufferView.CreateWholeBuffer(buffer.BufferId, SizedInternalFormat.R32f);
#if DEBUG
            if (generatedName) Assert.True(cache.TextureBindCount > previousBindCount);
            else Assert.Equal(previousBindCount, cache.TextureBindCount);
#endif
            Assert.Equal((int)TextureUnit.Texture3, GL.GetInteger(GetPName.ActiveTexture));
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBindingBuffer));
            GL.ActiveTexture(TextureUnit.Texture0);
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBindingBuffer));
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.GetTextureLevelParameter(view.TextureId, 0, (GetTextureParameter)All.TextureBufferDataStoreBindingExt, out int actualBuffer);
            GL.GetTextureLevelParameter(view.TextureId, 0, GetTextureParameter.TextureInternalFormat, out int actualFormat);
            Assert.Equal(actualBuffer, view.BufferId);
            Assert.Equal(buffer.BufferId, actualBuffer);
            Assert.Equal((SizedInternalFormat)actualFormat, view.Format);
            if (ranged)
            {
                GL.GetTextureLevelParameter(view.TextureId, 0, (GetTextureParameter)All.TextureBufferOffset, out int actualOffset);
                GL.GetTextureLevelParameter(view.TextureId, 0, (GetTextureParameter)All.TextureBufferSize, out int actualSize);
                Assert.Equal((nint)actualOffset, view.OffsetBytes);
                Assert.Equal((nint)actualSize, view.SizeBytes);
                Assert.Equal((nint)offset, view.OffsetBytes);
                Assert.Equal((nint)size, view.SizeBytes);
            }
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(view));
            using var alias = new TextureAlias(view.TextureId);
            manager.AssociateResource(entry, alias);
            Assert.Equal(entry.Metadata.Buffer, alias.Buffer);
            Assert.Equal(new GpuTextureBufferMetadata(buffer.BufferId, SizedInternalFormat.R32f,
                ranged ? offset : 0, ranged ? size : 0, !ranged), alias.Buffer);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.ActiveTexture(TextureUnit.Texture0); cache.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Starts an isolated optional manager lifetime after removing any fixture-owned service.</summary>
    private Lifetime StartLifetime(bool managed)
    {
        fixture.EnsureContextValid();
        fixture.CleanupGpuResources();
        return new Lifetime(managed);
    }

    /// <summary>Provides a non-owning typed recipient without implementing any metadata acquisition logic.</summary>
    private sealed class BufferAlias : GpuResource<GpuBufferMetadata>
    {
        /// <summary>Retains only the identity of the existing allocation.</summary>
        internal BufferAlias(int id) { ResourceId = id; }
        /// <summary>Gets the last manager-delivered capacity.</summary>
        internal nint Capacity => metadata.Capacity;
        /// <summary>Gets the last manager-delivered storage usage.</summary>
        internal BufferUsageHint Usage => metadata.Usage;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
    }

    /// <summary>Exercises manager-owned construction through the generated-name fallback.</summary>
    private sealed class GeneratedBufferView : GpuBufferView
    {
        /// <summary>Initializes a generated name through the production bound fallback.</summary>
        internal GeneratedBufferView(int buffer, nint offset, nint size)
            : base() { InitializeBufferView(buffer, SizedInternalFormat.R32f, offset, size, null); }
    }

    /// <summary>Receives texture metadata through the manager without acquiring native ownership.</summary>
    private sealed class TextureAlias : GpuResource<GpuTextureMetadata>
    {
        /// <summary>Retains an existing native identity for typed snapshot delivery.</summary>
        internal TextureAlias(int id) { ResourceId = id; }
        /// <summary>Gets the last manager-published buffer association.</summary>
        internal GpuTextureBufferMetadata? Buffer => metadata.Buffer;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
    }

    /// <summary>Contains service state so offline coverage cannot inherit a manager from another test.</summary>
    private sealed class Lifetime : IDisposable
    {
        private readonly GpuResourceManager? manager;
        /// <summary>Optionally installs a service in the current hidden context.</summary>
        internal Lifetime(bool managed)
        {
            Assert.Null(GpuResourceManagerSystem.CurrentManager);
            if (managed) { manager = new GpuResourceManager(); GpuResourceManagerSystem.Initialize(manager); }
        }
        /// <summary>Drains owning resources before closing registration and clearing cached bindings.</summary>
        public void Dispose()
        {
            manager?.Dispose();
            if (manager is not null) GpuResourceManagerSystem.Shutdown();
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
