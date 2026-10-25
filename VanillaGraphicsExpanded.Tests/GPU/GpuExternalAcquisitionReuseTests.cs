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
    private static int renderbufferQueries;
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
            using var buffer = GpuVbo.Create(); buffer.Allocate(64);
            using var view = GpuBufferView.CreateWholeBuffer(buffer.BufferId, SizedInternalFormat.R32f);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            int observed = 0;
            foreach (var method in typeof(GL).GetMethods().Where(method => method.Name is "GetTextureLevelParameter" or "GetTexLevelParameter"))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(GpuExternalAcquisitionReuseTests), nameof(ObserveAssociationRead)));
            associationRead = () =>
            {
                observed++;
                Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, view.TextureId, GpuTextureMetadataReader.Instance));
                Assert.False(manager.IsResourceMetadataAvailable(view));
                Assert.Throws<InvalidOperationException>(() => StateCache.BeginNativeBufferTexture(view.TextureId,
                    (int)TextureTarget.TextureBuffer, 0, true, false, buffer.BufferId, (int)SizedInternalFormat.R32f, 0, 0, true));
            };
            var operation = StateCache.BeginNativeBufferTexture(view.TextureId, (int)TextureTarget.TextureBuffer, 0,
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
        finally { associationRead = null; harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Local view and renderbuffer owners supply image projections without querying native storage again.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalViewAndRenderbufferProjectionUsesOwnerFacts(bool dsa)
    {
        fixture.EnsureContextValid();
        var harmony = new Harmony("Tests.LocalImageProjection." + Guid.NewGuid());
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(dsa);
            using var calibration = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
            using var texture = DynamicTexture3D.Create(8, 4, 6, PixelInternalFormat.Rgba8, immutableStorage: true);
            using var view = GpuTextureView.CreateFromTexture(texture, TextureTarget.Texture2DArray, PixelInternalFormat.Rgba8, 0, 1, 2, 3);
            using var renderbuffer = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 8, 4);
            foreach (var method in typeof(GL).GetMethods().Where(method => method.Name is "IsRenderbuffer" or "GetNamedRenderbufferParameter" or "GetRenderbufferParameter"))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(GpuExternalAcquisitionReuseTests), nameof(CountRenderbufferQuery)));
            renderbufferQueries = 0;
            Assert.True(GL.IsRenderbuffer(renderbuffer.RenderbufferId));
            Assert.True(renderbufferQueries > 0);
            renderbufferQueries = 0;
            using var observer = new ResourceCallObserver(forceEnabled: true);
            observer.Calibrate(calibration.TextureId);
            using var selectedView = GpuFramebufferAttachment.FromTexture(view, layer: 1);
            using var selectedRenderbuffer = GpuFramebufferAttachment.FromRenderbuffer(renderbuffer);
            selectedView.ValidateSlot(FramebufferAttachment.ColorAttachment0);
            selectedRenderbuffer.ValidateSlot(FramebufferAttachment.ColorAttachment0);
            Assert.Equal(8, selectedView.Width);
            Assert.Equal(4, selectedRenderbuffer.Height);
            AssertNoTextureQueries(observer);
            Assert.Equal(0, renderbufferQueries);
            GpuResourceManager.InvalidateWrapperMetadata(view);
            GpuResourceManager.InvalidateWrapperMetadata(renderbuffer);
            Assert.Throws<InvalidOperationException>(() => selectedView.ValidateSlot(FramebufferAttachment.ColorAttachment0));
            Assert.Throws<InvalidOperationException>(() => selectedRenderbuffer.ValidateSlot(FramebufferAttachment.ColorAttachment0));
            Assert.False(selectedView.IsValid);
            Assert.Equal(0, selectedRenderbuffer.Width);
            AssertNoTextureQueries(observer);
            Assert.Equal(0, renderbufferQueries);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Native observer execution rejects alias recovery and reentry until interrupted dispatch releases its guard.</summary>
    [Fact]
    public void NativeObserversBlockPartialAcquisitionAndReentry()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = DynamicTexture2D.Create(8, 4, PixelInternalFormat.Rgba8);
            using var renderbuffer = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 8, 4);
            using var vao = GpuVao.Create();
            using var buffer = GpuVbo.Create();
            buffer.Allocate(64);
            using var view = GpuBufferView.CreateWholeBuffer(buffer.BufferId, SizedInternalFormat.R32f);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var textureOperation = StateCache.BeginNativeTextureMetadata(texture.TextureId, (int)TextureTarget.Texture2D, 0,
                true, false, StateCache.TextureMetadataEffect.Storage, 0);
            try
            {
                Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, texture.TextureId, GpuTextureMetadataReader.Instance));
                Assert.Throws<InvalidOperationException>(() => StateCache.BeginNativeTextureMetadata(texture.TextureId,
                    (int)TextureTarget.Texture2D, 0, true, false, StateCache.TextureMetadataEffect.Storage, 0));
            }
            finally { textureOperation.Complete(false); }
            Assert.False(Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(texture)).OperationInProgress);
            Assert.False(manager.IsResourceMetadataAvailable(texture));
            var renderbufferOperation = StateCache.BeginNativeRenderbufferStorage(renderbuffer.RenderbufferId, true);
            try
            {
                Assert.Null(manager.AcquireResource(GpuResourceKind.Renderbuffer, renderbuffer.RenderbufferId, GpuRenderbufferMetadataReader.Instance));
                Assert.Throws<InvalidOperationException>(() => StateCache.BeginNativeRenderbufferStorage(renderbuffer.RenderbufferId, true));
            }
            finally { renderbufferOperation.Complete(false); }
            Assert.False(Assert.IsType<GpuResourceMetadataEntry<GpuRenderbufferMetadata>>(manager.GetResourceRegistration(renderbuffer)).OperationInProgress);
            var vertexOperation = StateCache.BeginNativeVertexMetadata(vao.VertexArrayId, true, false);
            try
            {
                Assert.Throws<InvalidOperationException>(() => StateCache.BeginNativeVertexMetadata(vao.VertexArrayId, true, false));
                Assert.False(manager.IsResourceMetadataAvailable(vao));
            }
            finally { vertexOperation.Complete(false); }
            Assert.False(Assert.IsType<GpuResourceMetadataEntry<GpuVertexArrayMetadata>>(manager.GetResourceRegistration(vao)).OperationInProgress);
            var viewOperation = StateCache.BeginNativeBufferTexture(view.TextureId, (int)TextureTarget.TextureBuffer, 0,
                true, false, buffer.BufferId, (int)SizedInternalFormat.R32f, 0, 0, true);
            try
            {
                Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, view.TextureId, GpuTextureMetadataReader.Instance));
                Assert.Throws<InvalidOperationException>(() => StateCache.BeginNativeBufferTexture(view.TextureId,
                    (int)TextureTarget.TextureBuffer, 0, true, false, buffer.BufferId, (int)SizedInternalFormat.R32f, 0, 0, true));
            }
            finally { viewOperation.Complete(false); }
            Assert.False(Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(view)).OperationInProgress);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Raw image and texture borrowers share available canonical facts without native descriptor or validity reads.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoveredRawAliasesReuseAvailableTexture(bool dsa)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(dsa);
            using var texture = DynamicTexture2D.Create(8, 4, PixelInternalFormat.Rgba8);
            using var observer = new ResourceCallObserver(forceEnabled: true);
            observer.Calibrate(texture.TextureId);
            using var borrowed = new BorrowedTexture(texture.TextureId);
            using var image = GpuFramebufferAttachment.FromTextureId(texture.TextureId);
            image.ValidateSlot(FramebufferAttachment.ColorAttachment0);
            Assert.Equal(8, borrowed.Width);
            Assert.Equal(4, image.Height);
            AssertNoTextureQueries(observer);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            Assert.Same(manager.GetResourceRegistration(texture), manager.GetResourceRegistration(borrowed));
            Assert.Same(manager.GetResourceRegistration(texture), manager.GetResourceRegistration(image));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Unknown native storage is discovered once; explicit withdrawal recovers once before covered reuse resumes.</summary>
    [Fact]
    public void UnknownAndUnavailableImagesRecoverThenReuse()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int native = GL.GenTexture();
        try
        {
            using (StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, native))
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            using var observer = new ResourceCallObserver(forceEnabled: true);
            observer.Calibrate(native);
            using var first = GpuFramebufferAttachment.FromTextureId(native);
            Assert.True(observer.Calls("GetTextureLevelParameter") > 0);
            observer.Reset();
            using var second = GpuFramebufferAttachment.FromTextureId(native);
            AssertNoTextureQueries(observer);
            using var borrowed = new BorrowedTexture(native);
            using (StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, native))
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 12, 6, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GpuResourceManager.InvalidateWrapperMetadata(borrowed, GpuMetadataChange.Storage);
            observer.Reset();
            using var recovered = GpuFramebufferAttachment.FromTextureId(native);
            Assert.Equal(12, recovered.Width);
            Assert.Equal(6, first.Height);
            Assert.True(observer.Calls("GetTextureLevelParameter") > 0);
            observer.Reset();
            using var stable = GpuFramebufferAttachment.FromTextureId(native);
            AssertNoTextureQueries(observer);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { StateCache.Current.DeleteTexture(native); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Local image validation uses the owner's latest supplied facts and clears a failed projection instead of rereading stale storage.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalOwnerProjectionAvoidsReadsAndRejectsWithdrawnFacts(bool dsa)
    {
        fixture.EnsureContextValid();
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(dsa);
            using var texture = DynamicTexture2D.Create(8, 4, PixelInternalFormat.Rgba8);
            using var observer = new ResourceCallObserver(forceEnabled: true);
            observer.Calibrate(texture.TextureId);
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            image.ValidateSlot(FramebufferAttachment.ColorAttachment0);
            image.ValidateSlot(FramebufferAttachment.ColorAttachment0);
            Assert.Equal(8, image.Width);
            AssertNoTextureQueries(observer);
            // Withdrawal deliberately leaves native storage intact. Projection must not silently
            // reacquire it and turn an interrupted owner's old allocation back into usable facts.
            GpuResourceManager.InvalidateWrapperMetadata(texture, GpuMetadataChange.Storage);
            Assert.Throws<InvalidOperationException>(() => image.ValidateSlot(FramebufferAttachment.ColorAttachment0));
            Assert.Equal(0, image.Width);
            AssertNoTextureQueries(observer);
            GpuResourceManager.ObserveWrapperMetadata(texture, new GpuTextureMetadataReader(TextureTarget.Texture2D), GpuMetadataChange.Metadata);
            observer.Reset();
            image.ValidateSlot(FramebufferAttachment.ColorAttachment0);
            Assert.Equal(8, image.Width);
            AssertNoTextureQueries(observer);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Exercises a reentrant consumer at the native association-query boundary.</summary>
    private static void ObserveAssociationRead() => associationRead?.Invoke();

    /// <summary>Counts renderbuffer descriptor and validity queries in the isolated local projection interval.</summary>
    private static void CountRenderbufferQuery() => renderbufferQueries++;

    /// <summary>Checks every texture validity and descriptor entry point observed by the shared calibrated fixture.</summary>
    private static void AssertNoTextureQueries(ResourceCallObserver observer)
    {
        Assert.Equal(0, observer.Calls("IsTexture"));
        Assert.Equal(0, observer.Calls("GetTextureParameter"));
        Assert.Equal(0, observer.Calls("GetTexParameter"));
        Assert.Equal(0, observer.Calls("GetTextureLevelParameter"));
        Assert.Equal(0, observer.Calls("GetTexLevelParameter"));
    }
    #endregion
}
