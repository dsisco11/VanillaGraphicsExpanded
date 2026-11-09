using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies external aliases reuse covered facts and local images project supplied owner snapshots.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuExternalAcquisitionReuseTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>An association with unavailable backing withdraws until a later supplied association succeeds.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssociationWithUnavailableBackingRequiresExplicitRecovery(bool dsa)
    {
        // Arrange the owner, view, and metadata observer before withdrawing storage facts.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(dsa);
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); buffer.Allocate(64);
            using var view = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            using var reads = new ImageDescriptorCapture();
            // The supplied association cannot certify an unavailable backing.
            // Act by withdrawing backing storage and re-observing the supplied association.
            Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.FindResource(GpuResourceKind.Buffer, buffer.BufferId)).BeginRefresh(GpuMetadataChange.Storage);
            OpenGlStateCache.ObserveBufferTexture(view.TextureId, (int)TextureTarget.TextureBuffer, 0,
                true, false, buffer.BufferId, (int)SizedInternalFormat.R32f, 0, 0, true);
            // Assert that association alone cannot restore availability; then perform the owner's explicit recovery.
            Assert.False(manager.IsResourceMetadataAvailable(view));
            Assert.NotNull(manager.RecoverResource(GpuResourceKind.Buffer, buffer.BufferId, new GpuBufferMetadataReader()));
            // Act by delivering the association again after the owner has supplied fresh metadata.
            OpenGlStateCache.ObserveBufferTexture(view.TextureId, (int)TextureTarget.TextureBuffer, 0,
                true, false, buffer.BufferId, (int)SizedInternalFormat.R32f, 0, 0, true);
            // Assert the recovered owner facts publish without another image-descriptor query.
            Assert.True(manager.IsResourceMetadataAvailable(view));
            Assert.Equal(buffer.BufferId, view.BufferId);
            Assert.Equal(0, reads.Reads);
            Assert.False(Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(view)).OperationInProgress);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    /// <summary>Local view and renderbuffer owners supply image projections that reject withdrawn metadata.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalViewAndRenderbufferProjectionUsesOwnerFacts(bool dsa)
    {
        // Arrange owner-backed view and renderbuffer images, then verify their projections before invalidation.
        fixture.EnsureContextValid();
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(dsa);
            using var texture = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 6), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = texture, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 2, LayerCount = 3, Label = null }).RequireResource();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            using var renderbuffer = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 8, Height = 4, Samples = 0, Label = null }).RequireResource();
            Assert.True(GL.IsRenderbuffer(renderbuffer.RenderbufferId));
            using var selectedView = GpuFramebufferAttachment.FromTexture(view, layer: 1);
            using var selectedRenderbuffer = GpuFramebufferAttachment.FromRenderbuffer(renderbuffer);
            selectedView.ValidateSlot(FramebufferSlot.Color0);
            selectedRenderbuffer.ValidateSlot(FramebufferSlot.Color0);
            Assert.Equal(8, selectedView.Width);
            Assert.Equal(4, selectedRenderbuffer.Height);
            // Act by withdrawing each owner's metadata before validating either image again.
            RHIResourceManager.InvalidateWrapperMetadata(view);
            RHIResourceManager.InvalidateWrapperMetadata(renderbuffer);
            // Assert that local projection rejects withdrawn facts instead of rediscovering them.
            Assert.Throws<InvalidOperationException>(() => selectedView.ValidateSlot(FramebufferSlot.Color0));
            Assert.Throws<InvalidOperationException>(() => selectedRenderbuffer.ValidateSlot(FramebufferSlot.Color0));
            Assert.False(selectedView.IsValid);
            Assert.Equal(0, selectedRenderbuffer.Width);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    /// <summary>Image observations publish supplied facts without acquisition; controlled failures release their execution guards.</summary>
    [Fact]
    public void NativeObserversBlockPartialAcquisitionAndReentry()
    {
        // Arrange callback readers on real texture and renderbuffer entries before native observers deliver facts.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var renderbuffer = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 8, Height = 4, Samples = 0, Label = null }).RequireResource();
            using var vao = NativeResourceSetup.CreateVertexArray();
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            using var view = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var textureEntry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(texture));
            int textureReads = 0;
            textureEntry.Reader = new CallbackReader<GpuTextureMetadata>(GpuTextureMetadataReader.Instance, () => textureReads++);
            // Act through the native observer; supplied facts must be enough to publish texture metadata.
            OpenGlStateCache.ObserveTextureDefinition(texture.TextureId, (int)TextureTarget.Texture2D, 0,
                true, false, 0, (int)PixelInternalFormat.Rgba8, 16, 8, 1, 1, false, 0, 0, 2);
            // Assert that the observer did not enter the acquisition callback.
            Assert.Equal(0, textureReads);
            Assert.True(manager.IsResourceMetadataAvailable(texture));
            Assert.Equal(16, textureEntry.Metadata.Levels[0].Width);

            var renderbufferEntry = Assert.IsType<GpuResourceMetadataEntry<GpuRenderbufferMetadata>>(manager.GetResourceRegistration(renderbuffer));
            int renderbufferReads = 0;
            renderbufferEntry.Reader = new CallbackReader<GpuRenderbufferMetadata>(GpuRenderbufferMetadataReader.Instance, () => renderbufferReads++);
            // Act through renderbuffer storage observation with the same callback guard.
            OpenGlStateCache.ObserveRenderbufferStorage(renderbuffer.RenderbufferId, true, (int)RenderbufferStorage.Rgba8, 16, 8, 0);
            // Assert that the supplied renderbuffer facts remain usable without recovery.
            Assert.Equal(0, renderbufferReads);
            Assert.True(renderbufferEntry.IsUsable);
            Assert.Equal(16, renderbufferEntry.Metadata.Width);

            var vertexEntry = Assert.IsType<GpuResourceMetadataEntry<GpuVertexArrayMetadata>>(manager.GetResourceRegistration(vao));
            // Act by failing vertex configuration after its transaction has acquired the VAO guard.
            Assert.Throws<ApplicationException>(() => OpenGLVertexConfiguration.Execute(OpenGlStateCache.Current, vao.VertexArrayId, false, () =>
            {
                Assert.True(vertexEntry.OperationInProgress);
                Assert.False(manager.IsResourceMetadataAvailable(vao));
                Assert.Throws<InvalidOperationException>(() => OpenGlStateCache.Current.RequireResourceOperationAvailable(GpuResourceKind.VertexArray, vao.VertexArrayId));
                throw new ApplicationException("Injected controlled configuration failure.");
            }));
            // Assert that failure releases the guard while leaving acquisition unavailable.
            Assert.False(vertexEntry.OperationInProgress);
            Assert.False(manager.IsResourceMetadataAvailable(vao));
            OpenGlStateCache.Current.RequireResourceOperationAvailable(GpuResourceKind.VertexArray, vao.VertexArrayId);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    /// <summary>Raw image and texture borrowers share available canonical facts and original registration.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoveredRawAliasesReuseAvailableTexture(bool dsa)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        // Arrange an owned texture and acquire both a raw texture borrower and framebuffer-image alias.
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(dsa);
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var borrowed = new BorrowedTexture(texture.TextureId);
            using var image = GpuFramebufferAttachment.FromTextureId(texture.TextureId);
            // Act by validating the image alias against its color attachment slot.
            image.ValidateSlot(FramebufferSlot.Color0);
            // Assert the aliases expose dimensions and all resolve to the owner's registration.
            Assert.Equal(8, borrowed.Width);
            Assert.Equal(4, image.Height);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            Assert.Same(manager.GetResourceRegistration(texture), manager.GetResourceRegistration(borrowed));
            Assert.Same(manager.GetResourceRegistration(texture), manager.GetResourceRegistration(image));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    /// <summary>Local image validation uses the owner's latest facts and clears failed projections.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalOwnerProjectionRejectsWithdrawnFacts(bool dsa)
    {
        // Arrange an unregistered owner and validate its image from the owner's supplied storage snapshot.
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(dsa);
            using var texture = ContextLocalTexture.Create(8, 4, TextureFormat.Rgba8);
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            image.ValidateSlot(FramebufferSlot.Color0);
            image.ValidateSlot(FramebufferSlot.Color0);
            Assert.Equal(8, image.Width);
            // Withdrawal deliberately leaves native storage intact. Projection must not silently
            // reacquire it and turn an interrupted owner's old allocation back into usable facts.
            // Act by withdrawing the owner's snapshot and validating the existing image again.
            RHIResourceManager.InvalidateWrapperMetadata(texture, GpuMetadataChange.Storage);
            // Assert that the old image fails closed and direct wrapper recovery is rejected.
            Assert.Throws<InvalidOperationException>(() => image.ValidateSlot(FramebufferSlot.Color0));
            Assert.Equal(0, image.Width);
            Assert.Throws<InvalidOperationException>(() => RHIResourceManager.RecoverWrapperMetadata(texture, new GpuTextureMetadataReader(TextureDimension.Two)));
            // Explicit owner replacement supplies fresh facts; the withdrawn original stays unavailable.
            using var replacement = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
            { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 8, Height = 4, MipLevels = 1,
                Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0 }).RequireResource();
            using var replacementImage = GpuFramebufferAttachment.FromTexture(replacement);
            // Act on a fresh owner to verify a replacement can publish its own valid projection.
            replacementImage.ValidateSlot(FramebufferSlot.Color0);
            // Assert the replacement is valid while the withdrawn image remains unavailable.
            Assert.Equal(8, replacementImage.Width);
            Assert.Throws<InvalidOperationException>(() => image.ValidateSlot(FramebufferSlot.Color0));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
    #region Private
    /// <summary>Invokes a consumer during actual metadata acquisition to test completion exclusion and interruption.</summary>
    private sealed class CallbackReader<T>(IGpuResourceMetadataReader<T> inner, Action callback) : IGpuResourceMetadataReader<T> where T : struct
    {
        #region Public API
        /// <inheritdoc />
        public bool TryRead(nint id, out T metadata)
        {
            callback();
            return inner.TryRead(id, out metadata);
        }
        #endregion
    }

    #endregion
}
