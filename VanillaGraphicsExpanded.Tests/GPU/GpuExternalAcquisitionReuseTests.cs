using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies external aliases reuse covered facts and local images project supplied owner snapshots.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuExternalAcquisitionReuseTests(HeadlessGLFixture fixture)
{
    private static Action? associationRead;
    #region Public API
    /// <summary>Association certification keeps the original operation guarded while native result queries execute.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssociationResultReadRejectsRecoveryAndReentry(bool dsa)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.AssociationReadGuard." + Guid.NewGuid());
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(dsa);
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); buffer.Allocate(64);
            using var view = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            int observed = 0;
            foreach (var method in typeof(OpenTK.Graphics.OpenGL4.GL).GetMethods().Where(method => method.Name == "GetTextureLevelParameter"))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(GpuExternalAcquisitionReuseTests), nameof(ObserveAssociationRead)));
            associationRead = () =>
            {
                observed++;
                Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, view.TextureId, GpuTextureMetadataReader.Instance));
                Assert.False(manager.IsResourceMetadataAvailable(view));
                Assert.Throws<InvalidOperationException>(() => OpenGlStateCache.BeginNativeBufferTexture(view.TextureId,
                    (int)TextureTarget.TextureBuffer, 0, true, false, buffer.BufferId, (int)SizedInternalFormat.R32f, 0, 0, true));
            };
            var operation = OpenGlStateCache.BeginNativeBufferTexture(view.TextureId, (int)TextureTarget.TextureBuffer, 0,
                true, false, buffer.BufferId, (int)SizedInternalFormat.R32f, 0, 0, true);
            // The unchanged valid association is enough to test certification without altering
            // content or introducing a second intercepted mutation into the measured interval.
            operation.Complete(true);
            associationRead = null;
            Assert.True(observed > 0);
            Assert.True(manager.IsResourceMetadataAvailable(view));
            Assert.Equal(buffer.BufferId, view.BufferId);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { associationRead = null; harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Local view and renderbuffer owners supply image projections that reject withdrawn metadata.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalViewAndRenderbufferProjectionUsesOwnerFacts(bool dsa)
    {
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
            GpuResourceManager.InvalidateWrapperMetadata(view);
            GpuResourceManager.InvalidateWrapperMetadata(renderbuffer);
            Assert.Throws<InvalidOperationException>(() => selectedView.ValidateSlot(FramebufferSlot.Color0));
            Assert.Throws<InvalidOperationException>(() => selectedRenderbuffer.ValidateSlot(FramebufferSlot.Color0));
            Assert.False(selectedView.IsValid);
            Assert.Equal(0, selectedRenderbuffer.Width);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Native execution rejects partial acquisition and unrelated reentry until its guard is released.</summary>
    [Fact]
    public void NativeObserversBlockPartialAcquisitionAndReentry()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var renderbuffer = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 8, Height = 4, Samples = 0, Label = null }).RequireResource();
            using var vao = NativeResourceSetup.CreateVertexArray();
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            using var view = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var textureOperation = OpenGlStateCache.BeginNativeTextureMetadata(texture.TextureId, (int)TextureTarget.Texture2D, 0,
                true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
            try
            {
                Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, texture.TextureId, GpuTextureMetadataReader.Instance));
                Assert.Throws<InvalidOperationException>(() => OpenGlStateCache.BeginNativeTextureMetadata(texture.TextureId,
                    (int)TextureTarget.Texture2D, 0, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0));
            }
            finally { textureOperation.Complete(false); }
            Assert.False(Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(texture)).OperationInProgress);
            Assert.False(manager.IsResourceMetadataAvailable(texture));
            var renderbufferOperation = OpenGlStateCache.BeginNativeRenderbufferStorage(renderbuffer.RenderbufferId, true);
            try
            {
                Assert.Null(manager.AcquireResource(GpuResourceKind.Renderbuffer, renderbuffer.RenderbufferId, GpuRenderbufferMetadataReader.Instance));
                Assert.Throws<InvalidOperationException>(() => OpenGlStateCache.BeginNativeRenderbufferStorage(renderbuffer.RenderbufferId, true));
            }
            finally { renderbufferOperation.Complete(false); }
            Assert.False(Assert.IsType<GpuResourceMetadataEntry<GpuRenderbufferMetadata>>(manager.GetResourceRegistration(renderbuffer)).OperationInProgress);
            var vertexOperation = OpenGlStateCache.BeginNativeVertexMetadata(vao.VertexArrayId, true, false);
            try
            {
                Assert.Throws<InvalidOperationException>(() => OpenGlStateCache.BeginNativeVertexMetadata(vao.VertexArrayId, true, false));
                Assert.False(manager.IsResourceMetadataAvailable(vao));
            }
            finally { vertexOperation.Complete(false); }
            Assert.False(Assert.IsType<GpuResourceMetadataEntry<GpuVertexArrayMetadata>>(manager.GetResourceRegistration(vao)).OperationInProgress);
            var viewOperation = OpenGlStateCache.BeginNativeBufferTexture(view.TextureId, (int)TextureTarget.TextureBuffer, 0,
                true, false, buffer.BufferId, (int)SizedInternalFormat.R32f, 0, 0, true);
            try
            {
                Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, view.TextureId, GpuTextureMetadataReader.Instance));
                Assert.Throws<InvalidOperationException>(() => OpenGlStateCache.BeginNativeBufferTexture(view.TextureId,
                    (int)TextureTarget.TextureBuffer, 0, true, false, buffer.BufferId, (int)SizedInternalFormat.R32f, 0, 0, true));
            }
            finally { viewOperation.Complete(false); }
            Assert.False(Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(view)).OperationInProgress);
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
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(dsa);
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var borrowed = new BorrowedTexture(texture.TextureId);
            using var image = GpuFramebufferAttachment.FromTextureId(texture.TextureId);
            image.ValidateSlot(FramebufferSlot.Color0);
            Assert.Equal(8, borrowed.Width);
            Assert.Equal(4, image.Height);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            Assert.Same(manager.GetResourceRegistration(texture), manager.GetResourceRegistration(borrowed));
            Assert.Same(manager.GetResourceRegistration(texture), manager.GetResourceRegistration(image));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Unknown and withdrawn native storage recovers coherent facts across image aliases.</summary>
    [Fact]
    public void UnknownAndUnavailableImagesRecoverThenReuse()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int native = GL.GenTexture();
        try
        {
            using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, native))
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            using var first = GpuFramebufferAttachment.FromTextureId(native);
            using var second = GpuFramebufferAttachment.FromTextureId(native);
            Assert.Equal(first.Width, second.Width);
            Assert.Same(GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(first),
                GpuResourceManagerSystem.CurrentManager.GetResourceRegistration(second));
            using var borrowed = new BorrowedTexture(native);
            using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, native))
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 12, 6, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GpuResourceManager.InvalidateWrapperMetadata(borrowed, GpuMetadataChange.Storage);
            using var recovered = GpuFramebufferAttachment.FromTextureId(native);
            Assert.Equal(12, recovered.Width);
            Assert.Equal(6, first.Height);
            using var stable = GpuFramebufferAttachment.FromTextureId(native);
            Assert.Equal(recovered.Width, stable.Width);
            Assert.Equal(recovered.Height, stable.Height);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, native)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Local image validation uses the owner's latest facts and clears failed projections.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalOwnerProjectionRejectsWithdrawnFacts(bool dsa)
    {
        fixture.EnsureContextValid();
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(dsa);
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            image.ValidateSlot(FramebufferSlot.Color0);
            image.ValidateSlot(FramebufferSlot.Color0);
            Assert.Equal(8, image.Width);
            // Withdrawal deliberately leaves native storage intact. Projection must not silently
            // reacquire it and turn an interrupted owner's old allocation back into usable facts.
            GpuResourceManager.InvalidateWrapperMetadata(texture, GpuMetadataChange.Storage);
            Assert.Throws<InvalidOperationException>(() => image.ValidateSlot(FramebufferSlot.Color0));
            Assert.Equal(0, image.Width);
            GpuResourceManager.ObserveWrapperMetadata(texture, new GpuTextureMetadataReader(TextureDimension.Two), GpuMetadataChange.Metadata);
            image.ValidateSlot(FramebufferSlot.Color0);
            Assert.Equal(8, image.Width);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Exercises a reentrant consumer at the native association-query boundary.</summary>
    private static void ObserveAssociationRead() => associationRead?.Invoke();
    #endregion
}
