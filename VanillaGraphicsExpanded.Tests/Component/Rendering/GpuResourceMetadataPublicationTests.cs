using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies canonical texture and framebuffer metadata publication across managed operation lifetimes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuResourceMetadataPublicationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Complete cube storage publishes all face and mip descriptors as one revision.</summary>
    [Fact]
    public void CubeAllocationPublishesCompleteMipChainOnce()
    {
        // Arrange: install the metadata manager used to observe the allocation's publication revisions.
        using var lifetime = StartLifetime(true);

        // Act: allocate all six faces across three mip levels through the production texture wrapper.
        using var cube = RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = TextureFormat.Rgba16f, Width = 8, Height = 8, MipLevels = 3, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (3) - 1, Label = null }).RequireResource();

        // Assert: one complete descriptor set is visible after construction.
        var manager = RHIResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(cube));
        Assert.True(entry.MetadataAvailable);
        Assert.Equal((ulong)1, entry.MetadataRevision);
        Assert.Equal((ulong)0, entry.StorageRevision);
        Assert.Equal(18, entry.Metadata.Levels.Length);
        Assert.All(entry.Metadata.Levels, image => Assert.True(image.Allocated));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Failed configuration withdraws incomplete metadata and retains the last committed descriptor.</summary>
    [Fact]
    public void FailedAttachmentOperationLeavesCanonicalMetadataUnavailable()
    {
        using var lifetime = StartLifetime(true);
        // Arrange: retain the original attachment descriptor so recovery can be checked after a rejected update.
        using var originalTexture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var replacementTexture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var original = GpuFramebufferAttachment.FromTexture(originalTexture);
        using var replacement = GpuFramebufferAttachment.FromTexture(replacementTexture);
        using var framebuffer = DynamicGpuFramebuffer.Create([original])!;
        var cache = OpenGlStateCache.Current;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(manager.GetResourceRegistration(framebuffer));
        ulong revision = entry.MetadataRevision;
        var previous = entry.Metadata.Attachments[FramebufferSlot.Color0];
        using var bindings = cache.BindFramebufferScope();

        // Act: begin a replacement and then submit an invalid depth slot in the same operation.
        framebuffer.Bind();
        try
        {
            using (var operation = new OpenGLFramebufferConfiguration.FramebufferOperation(framebuffer))
            {
                OpenGLFramebufferConfiguration.FramebufferTexture(framebuffer, FramebufferSlot.Color0, replacement, operation);
                Assert.False(entry.MetadataAvailable);
                Assert.Equal(revision, entry.MetadataRevision);
                var invalid = FramebufferSlot.Depth;
                Assert.Throws<ArgumentException>(() => OpenGLFramebufferConfiguration.FramebufferTexture(framebuffer, invalid, replacement, operation));
            }
            // Assert: intermediate publication is withdrawn until the failed operation completes.
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal(previous, entry.Metadata.Attachments[FramebufferSlot.Color0]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { while (GL.GetError() != ErrorCode.NoError) { } }
    }
    #endregion

    #region Private
    /// <summary>Starts an isolated manager lifetime after removing fixture-owned global services.</summary>
    private Lifetime StartLifetime(bool managed)
    {
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        return new Lifetime(managed);
    }

    /// <summary>Contains an optional manager so each test begins without process-global service residue.</summary>
    private sealed class Lifetime(bool managed) : IDisposable
    {
        private readonly RHIResourceManager? manager = CreateManager(managed);

        #region Public API
        /// <summary>Releases owning resources before clearing the optional manager and binding cache.</summary>
        public void Dispose()
        {
            manager?.Dispose();
            if (manager is not null) RHIResourceManagerSystem.Shutdown();
            OpenGlStateCache.Current.InvalidateAll();
        }
        #endregion

        #region Private
        /// <summary>Initializes a manager only when the selected scenario exercises registration publication.</summary>
        private static RHIResourceManager? CreateManager(bool enabled)
        {
            Assert.Null(RHIResourceManagerSystem.CurrentManager);
            if (!enabled) return null;
            var manager = new RHIResourceManager();
            RHIResourceManagerSystem.Initialize(manager);
            return manager;
        }
        #endregion
    }
    #endregion
}
