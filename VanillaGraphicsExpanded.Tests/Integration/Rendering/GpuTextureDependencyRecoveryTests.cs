using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks dependent notification and allocation identity across local metadata failures.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuTextureDependencyRecoveryTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>An invalid image cannot prevent another image and its framebuffer from observing replacement.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidImageDoesNotStarveValidImage(bool retiredManager)
    {
        // Arrange: Attach valid and invalid layers to separate framebuffers under the selected manager state.
        PrepareContext(retiredManager);
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var invalid = GpuFramebufferAttachment.FromTexture(source, layer: 1);
            using var valid = GpuFramebufferAttachment.FromTexture(source, layer: 0);
            using var first = DynamicGpuFramebuffer.CreateEmpty();
            using var second = DynamicGpuFramebuffer.CreateEmpty();
            first.SetAttachment(FramebufferSlot.Color0, invalid);
            second.SetAttachment(FramebufferSlot.Color0, valid);
            ulong invalidRevision = first.AttachmentRevision;
            ulong validRevision = second.AttachmentRevision;
            // Act: Shrink the array to one layer, then bind each dependent framebuffer independently.
            Assert.True(source.Resize(8, 4, 1));
            Assert.Equal(8, source.Width);
            Assert.Equal(8, valid.Width);
            Assert.Equal(0, invalid.Width);
            Assert.True(first.AttachmentRevision > invalidRevision);
            Assert.True(second.AttachmentRevision > validRevision);
            // Assert: The invalid dependency rejects while the valid dependency resolves to the replacement source.
            Assert.Throws<InvalidOperationException>(() => first.Bind());
            second.Bind();
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferParameterName.FramebufferAttachmentObjectName, out int attached);
            Assert.Equal(source.TextureId, attached);
            Assert.True(second.CheckStatus(out _));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Controlled mip selection preserves an existing view without requiring descriptor acquisition.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ControlledMipRangePreservesViewWithoutRecovery(bool retiredManager, bool throws)
    {
        // Arrange: Create a view and install a reader that would fail if mip-range selection tried to reacquire metadata.
        PrepareContext(retiredManager);
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
            int viewId = view.TextureId;
            using (var fault = new NativeReadFailure(throws))
            {
                // Act: Change the controlled mip range while the acquisition fault is active.
                source.SetMipRange(0, 0);
                Assert.Equal(0, fault.Reads);
            }
            // Assert: No reader call occurred, and the same native view remains valid across the range change.
            Assert.True(view.IsValid);
            source.SetMipRange(0, 0);
            Assert.Equal(4, source.Width);
            Assert.True(view.IsValid);
            Assert.Equal(viewId, view.TextureId);
            Assert.True(GL.IsTexture(viewId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Only an interrupted storage operation retires views; interrupted parameter publication can recover.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InterruptedOperationPreservesOnlyMetadataIdentity(bool retiredManager, bool storage)
    {
        // Arrange: Create a dependent view and publish an incomplete metadata or storage operation.
        PrepareContext(retiredManager);
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
            // An incomplete operation cannot certify whether native storage commands executed before failure.
            using (new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(source, storage ? GpuMetadataChange.Storage : GpuMetadataChange.Metadata)) { }
            // Act: Attempt range mutation before and after explicit metadata recovery.
            Assert.False(view.IsValid);
            Assert.Throws<InvalidOperationException>(() => source.SetMipRange(0, 0));
            RHIResourceManager.RecoverWrapperMetadata(source, new GpuTextureMetadataReader(TextureDimension.TwoArray));
            source.SetMipRange(0, 0);
            Assert.Equal(4, source.Width);
            // Assert: Recovery restores parameter-only identity, while uncertain storage identity keeps the view retired.
            Assert.Equal(!storage, view.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Equal image dimensions do not allow an old view certificate to survive a storage publication.</summary>
    [Fact]
    public void EqualSizedStoragePublicationRetiresView()
    {
        // Arrange: Retain a view and snapshot while the source has a known allocation.
        PrepareContext(false);
        using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
        var before = source.StorageImages;
        // Act: Publish the same observed image facts as a new storage event, without resizing the allocation.
        using (var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(source, GpuMetadataChange.Storage))
        {
            Assert.True(new GpuTextureMetadataReader(source.TextureTarget).TryRead(source.TextureId, out var candidate));
            operation.Commit(candidate);
        }
        // Assert: Equal image dimensions do not preserve a view certificate across storage publication.
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
        fixture.RetireResourceServices();
        Assert.Null(RHIResourceManagerSystem.CurrentManager);
        if (!retiredManager) return;
        fixture.InitializeResourceDisposal();
        RHIResourceManagerSystem.CurrentManager!.Dispose();
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
