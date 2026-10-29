using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies maintained import composition and supplied local owner-image delivery.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ResourceAcquisitionBoundaryTests(HeadlessGLFixture fixture)
{
    private static Action? duringProjection;
    #region Public API
    /// <summary>In-place storage changes notify each dependent once with registry or genuine local projection delivery.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizePublishesSelectedImageExactlyOnce(bool tracked)
    {
        fixture.EnsureContextValid();
        if (tracked) fixture.InitializeResourceDisposal(); else fixture.UseContextLocalResources();
        try
        {
            using var owner = tracked
                ? RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
                { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 4, Height = 4,
                    MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0 }).RequireResource()
                : ContextLocalTexture.Create(4, 4, TextureFormat.Rgba8);
            using var image = GpuFramebufferAttachment.FromTexture(owner);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            Assert.Equal(tracked, RHIResourceManagerSystem.CurrentManager is not null);
            int notifications = 0;
            framebuffer.AttachmentsChanged += () => notifications++;
            Assert.True(owner.Resize(8, 4));
            Assert.Equal(1, notifications);
            Assert.Equal(8, image.Width);
            image.ValidateSlot(FramebufferSlot.Color0);
            Assert.Equal(1, notifications);
            Assert.Equal(tracked, RHIResourceManagerSystem.CurrentManager is not null);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Covered external publication emits one event with no descriptor reads or borrowed disposal authority.</summary>
    [Fact]
    public void ImportedFramebufferPublicationIsOneTransaction()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
            { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 4, Height = 4,
                MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(owner);
            using var source = DynamicGpuFramebuffer.Create([image])!;
            using var reads = new ImageDescriptorCapture();
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
                Assert.Equal(0, reads.Reads);
            }
            Assert.True(source.IsValid);
            Assert.True(owner.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Image composition remains guarded without readback; managed handoff failure withdraws facts until explicit recovery.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ImportedCompositionGuardsAndWithdrawsFacts(bool fail)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.ResourceImport." + Guid.NewGuid());
        try
        {
            using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
            { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 4, Height = 4,
                MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(owner);
            using var source = DynamicGpuFramebuffer.Create([image])!;
            using var imported = DynamicGpuFramebuffer.Wrap(source.FboId, width: 4, height: 4);
            imported.PublishRenderPassMetadata();
            using var reads = new ImageDescriptorCapture();
            int notifications = 0, projections = 0;
            imported.AttachmentsChanged += () => notifications++;
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var original = manager.GetResourceRegistration(imported)!;
            var failure = new InvalidOperationException("Injected image-composition failure.");
            duringProjection = () =>
            {
                projections++;
                Assert.Throws<InvalidOperationException>(() => imported.PublishRenderPassMetadata());
                Assert.False(manager.IsResourceMetadataAvailable(imported));
                Assert.Null(manager.AcquireResource(GpuResourceKind.Framebuffer, source.FboId, GpuFramebufferMetadataReader.Instance));
                if (fail) throw failure;
            };
            // Inject at the actual managed composition boundary, not an obsolete descriptor read.
            harmony.Patch(AccessTools.Method(typeof(RHIResourceManager), nameof(RHIResourceManager.ResolveFramebufferAttachments)),
                prefix: new HarmonyMethod(typeof(ResourceAcquisitionBoundaryTests), nameof(ObserveProjection)));
            if (fail)
            {
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => imported.PublishRenderPassMetadata()));
                Assert.False(imported.HasRenderPassMetadata);
                Assert.False(manager.IsResourceMetadataAvailable(imported));
            }
            else imported.PublishRenderPassMetadata();
            Assert.Equal(1, projections);
            Assert.Equal(1, notifications);
            Assert.Equal(0, reads.Reads);
            Assert.Same(original, manager.GetResourceRegistration(imported));
            duringProjection = null;
            harmony.UnpatchAll(harmony.Id);
            if (fail) RHIResourceManager.RecoverWrapperMetadata(imported, GpuFramebufferMetadataReader.Instance);
            int recoveryReads = reads.Reads;
            imported.PublishRenderPassMetadata();
            Assert.True(imported.HasRenderPassMetadata);
            Assert.Equal(2, notifications);
            Assert.Equal(recoveryReads, reads.Reads);
            Assert.True(owner.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { duringProjection = null; harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Local image validation cannot recover withdrawn storage or borrow raw names without maintained registry facts.</summary>
    [Fact]
    public void LocalValidationDoesNotRecoverWithdrawnImage()
    {
        fixture.EnsureContextValid(); fixture.UseContextLocalResources();
        try
        {
            using var owner = ContextLocalTexture.Create(4, 4, TextureFormat.Rgba8);
            using var image = GpuFramebufferAttachment.FromTexture(owner);
            RHIResourceManager.InvalidateWrapperMetadata(owner);
            Assert.Equal(0, image.Width);
            Assert.Throws<InvalidOperationException>(() => image.ValidateSlot(FramebufferSlot.Color0));
            Assert.Throws<InvalidOperationException>(() => RHIResourceManager.RecoverWrapperMetadata(owner, GpuTextureMetadataReader.Instance));
            Assert.Equal(0, image.Width);
            using var replacement = ContextLocalTexture.Create(4, 4, TextureFormat.Rgba8);
            using var replacementImage = GpuFramebufferAttachment.FromTexture(replacement);
            replacementImage.ValidateSlot(FramebufferSlot.Color0);
            Assert.Equal(4, replacementImage.Width);
            Assert.Null(RHIResourceManagerSystem.CurrentManager);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Attempts reentry or failure while the existing import operation owns its image candidate.</summary>
    private static void ObserveProjection() => duringProjection?.Invoke();
    #endregion
}
