using VanillaGraphicsExpanded.Rendering.RHI;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks dependent notification and allocation identity across local metadata failures.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuTextureDependencyRecoveryTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>An invalid image cannot prevent another image and its framebuffer from observing replacement.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidImageDoesNotStarveValidImage(bool retiredManager)
    {
        PrepareContext(retiredManager);
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var invalid = GpuFramebufferAttachment.FromTexture(source, layer: 1);
            using var valid = GpuFramebufferAttachment.FromTexture(source, layer: 0);
            using var first = DynamicGpuFramebuffer.CreateEmpty();
            using var second = DynamicGpuFramebuffer.CreateEmpty();
            first.SetAttachment(FramebufferAttachment.ColorAttachment0, invalid);
            second.SetAttachment(FramebufferAttachment.ColorAttachment0, valid);
            ulong invalidRevision = first.AttachmentRevision;
            ulong validRevision = second.AttachmentRevision;
            var error = Assert.Throws<AggregateException>(() => source.Resize(8, 4, 1));
            Assert.IsType<InvalidOperationException>(Assert.Single(error.InnerExceptions));
            Assert.Equal(8, source.Width);
            Assert.Equal(8, valid.Width);
            Assert.Equal(0, invalid.Width);
            Assert.True(first.AttachmentRevision > invalidRevision);
            Assert.True(second.AttachmentRevision > validRevision);
            Assert.Throws<InvalidOperationException>(() => first.Bind());
            second.Bind();
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferParameterName.FramebufferAttachmentObjectName, out int attached);
            Assert.Equal(source.TextureId, attached);
            Assert.True(second.CheckStatus(out _));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Controlled mip selection preserves an existing view without requiring descriptor acquisition.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ControlledMipRangePreservesViewWithoutRecovery(bool retiredManager, bool throws)
    {
        PrepareContext(retiredManager);
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = (true ? RHIStorageMode.Immutable : RHIStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
            int viewId = view.TextureId;
            using (var fault = new NativeReadFailure(throws))
            {
                source.SetMipRange(0, 0);
                Assert.Equal(0, fault.Reads);
            }
            Assert.True(view.IsValid);
            source.SetMipRange(0, 0);
            Assert.Equal(4, source.Width);
            Assert.True(view.IsValid);
            Assert.Equal(viewId, view.TextureId);
            Assert.True(GL.IsTexture(viewId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Only an interrupted storage operation retires views; interrupted parameter publication can recover.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InterruptedOperationPreservesOnlyMetadataIdentity(bool retiredManager, bool storage)
    {
        PrepareContext(retiredManager);
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = (true ? RHIStorageMode.Immutable : RHIStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
            // An incomplete operation cannot certify whether native storage commands executed before failure.
            using (new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(source, storage ? GpuMetadataChange.Storage : GpuMetadataChange.Metadata)) { }
            Assert.False(view.IsValid);
            source.SetMipRange(0, 0);
            Assert.Equal(4, source.Width);
            Assert.Equal(!storage, view.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Equal image dimensions do not allow an old view certificate to survive a storage publication.</summary>
    [Fact]
    public void EqualSizedStoragePublicationRetiresView()
    {
        PrepareContext(false);
        using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = (true ? RHIStorageMode.Immutable : RHIStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
        var before = source.StorageImages;
        using (var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(source, GpuMetadataChange.Storage))
        {
            Assert.True(new GpuTextureMetadataReader(source.TextureTarget).TryRead(source.TextureId, out var candidate));
            operation.Commit(candidate);
        }
        Assert.True(before.SequenceEqual(source.StorageImages));
        Assert.False(view.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Selects an absent or retired installed manager, both of which use local acquisition.</summary>
    private void PrepareContext(bool retiredManager)
    {
        fixture.EnsureContextValid();
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        if (!retiredManager) return;
        fixture.InitializeResourceDisposal();
        GpuResourceManagerSystem.CurrentManager!.Dispose();
    }

    /// <summary>Injects a reader failure without changing the source allocation.</summary>
    private sealed class NativeReadFailure : IDisposable
    {
        private readonly Harmony harmony = new("VGE.Tests.TextureDependencyRecovery");
        private static bool throwOnRead;
        private static int reads;
        internal int Reads => reads;

        #region Public API
        /// <summary>Installs the collection-serialized acquisition fault.</summary>
        public NativeReadFailure(bool throws)
        {
            throwOnRead = throws;
            reads = 0;
            harmony.Patch(AccessTools.Method(typeof(GpuTextureMetadataReader), nameof(GpuTextureMetadataReader.TryRead)),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeReadFailure), nameof(Read))));
        }

        /// <summary>Restores native acquisition before attempting recovery.</summary>
        public void Dispose() => harmony.UnpatchAll(harmony.Id);
        #endregion

        #region Private
        /// <summary>Declines acquisition or throws while preserving all native resource state.</summary>
        private static bool Read(ref GpuTextureMetadata metadata, ref bool __result)
        {
            reads++;
            if (throwOnRead) throw new InvalidOperationException("Injected metadata read failure.");
            metadata = default;
            __result = false;
            return false;
        }
        #endregion
    }
    #endregion
}
