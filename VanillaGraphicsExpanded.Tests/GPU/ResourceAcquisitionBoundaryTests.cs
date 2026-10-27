using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies import publication and owner-image delivery use one admitted acquisition authority.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ResourceAcquisitionBoundaryTests(HeadlessGLFixture fixture)
{
    private static Action? duringRead;
    #region Public API
    /// <summary>In-place storage changes notify each dependent once with both registry and local projection delivery.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizePublishesSelectedImageExactlyOnce(bool tracked)
    {
        fixture.EnsureContextValid();
        if (tracked) fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 4, Height = 4, MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0 }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(owner);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            int notifications = 0;
            framebuffer.AttachmentsChanged += () => notifications++;
            Assert.True(owner.Resize(8, 4));
            Assert.Equal(1, notifications);
            Assert.Equal(8, image.Width);
            image.ValidateSlot(FramebufferSlot.Color0);
            Assert.Equal(1, notifications);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Explicit external refresh emits one event and borrowing never acquires disposal authority.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedFramebufferPublicationIsOneTransaction(bool tracked)
    {
        fixture.EnsureContextValid();
        if (tracked) fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 4, Height = 4, MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0 }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(owner);
            using var source = DynamicGpuFramebuffer.Create([image])!;
            using (var imported = DynamicGpuFramebuffer.Wrap(source.FboId, width: 4, height: 4))
            {
                int notifications = 0;
                imported.AttachmentsChanged += () => notifications++;
                imported.RefreshWrappedFramebuffer(source.FboId, 4, 4, true);
                Assert.Equal(1, notifications);
                var discovered = GpuFramebufferAttachmentDiscovery.Read(imported, FramebufferSlot.Color0);
                Assert.Equal(owner.TextureId, discovered.TextureId);
                Assert.False(discovered.OwnsStorage);
                Assert.Equal(1, notifications);
            }
            Assert.True(source.IsValid);
            Assert.True(owner.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Descriptor reads remain guarded until all images resolve; failure withdraws old usability and emits one event.</summary>
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void ImportedRecoveryGuardsAndWithdrawsFacts(bool tracked, bool fail)
    {
        fixture.EnsureContextValid();
        if (tracked) fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.ResourceImport." + Guid.NewGuid());
        try
        {
            using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
            {
                Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 4, Height = 4,
                MipLevels = 1, Storage = ResourceStorageMode.Mutable
            }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(owner);
            using var source = DynamicGpuFramebuffer.Create([image])!;
            using var imported = DynamicGpuFramebuffer.Wrap(source.FboId, width: 4, height: 4);
            imported.PublishRenderPassMetadata();
            int notifications = 0, reads = 0;
            imported.AttachmentsChanged += () => notifications++;
            duringRead = () =>
            {
                reads++;
                Assert.Throws<InvalidOperationException>(() => imported.PublishRenderPassMetadata());
                if (tracked)
                {
                    var manager = GpuResourceManagerSystem.CurrentManager!;
                    Assert.False(manager.IsResourceMetadataAvailable(imported));
                    Assert.Null(manager.AcquireResource(GpuResourceKind.Framebuffer, source.FboId, GpuFramebufferMetadataReader.Instance));
                }
                if (fail) throw new InvalidOperationException("Injected import failure.");
            };
            harmony.Patch(AccessTools.Method(typeof(OpenGLFramebufferQueries), nameof(OpenGLFramebufferQueries.TryRead)),
                postfix: new HarmonyMethod(typeof(ResourceAcquisitionBoundaryTests), nameof(ObserveFramebufferRead)));
            if (fail)
            {
                Assert.Throws<InvalidOperationException>(() => imported.PublishRenderPassMetadata());
                Assert.False(imported.HasRenderPassMetadata);
                if (tracked) Assert.False(GpuResourceManagerSystem.CurrentManager!.IsResourceMetadataAvailable(imported));
                else Assert.Null(imported.MetadataSnapshot.Attachments);
            }
            else imported.PublishRenderPassMetadata();
            Assert.Equal(1, reads);
            Assert.Equal(1, notifications);
            duringRead = null;
            harmony.UnpatchAll(harmony.Id);
            imported.PublishRenderPassMetadata();
            Assert.True(imported.HasRenderPassMetadata);
            Assert.Equal(2, notifications);
            Assert.True(owner.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { duringRead = null; harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Snapshot-only validation rejects withdrawn local image facts without recovering them implicitly.</summary>
    [Fact]
    public void LocalValidationDoesNotRecoverWithdrawnImage()
    {
        fixture.EnsureContextValid();
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 4, Height = 4, MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0 }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(owner);
        GpuResourceManager.InvalidateWrapperMetadata(owner);
        Assert.Equal(0, image.Width);
        Assert.Throws<InvalidOperationException>(() => image.ValidateSlot(FramebufferSlot.Color0));
        Assert.Equal(0, image.Width);
        // Only an explicit owner recovery can republish this local relationship.
        GpuResourceManager.RecoverWrapperMetadata(owner, GpuTextureMetadataReader.Instance);
        image.ValidateSlot(FramebufferSlot.Color0);
        Assert.Equal(4, image.Width);
    }
    #endregion
    #region Private
    /// <summary>Invokes the collection-serialized fault after native discovery while import admission is held.</summary>
    private static void ObserveFramebufferRead() => duringRead?.Invoke();
    #endregion
}
