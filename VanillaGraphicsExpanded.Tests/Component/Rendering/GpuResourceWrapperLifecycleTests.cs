using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies wrapper alias publication and managed rejection before invalid attachment dispatch.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuResourceWrapperLifecycleTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Rejected color slots do not publish a descriptor or reach native attachment dispatch.</summary>
    [Fact]
    public void RejectedFramebufferAttachmentDoesNotPublishInventedDescriptor()
    {
        // Arrange: create a valid framebuffer manager entry and obtain a slot outside the driver's supported range.
        using var lifetime = StartManager();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(texture);
        using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(
            lifetime.Manager.GetResourceRegistration(framebuffer));
        int limit = GL.GetInteger(GetPName.MaxColorAttachments);
        var slot = FramebufferSlot.Color0 + limit;

        // Act: attempt to attach at the unsupported slot.
        Assert.Throws<ArgumentException>(() => framebuffer.SetAttachment(slot, image));

        // Assert: managed metadata remains unchanged and the rejected call leaves no native error.
        Assert.False(entry.Metadata.Attachments?.ContainsKey(slot) ?? false);
        Assert.Null(framebuffer.GetAttachment(slot));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A manager-tracked image without storage is rejected before framebuffer use.</summary>
    [Fact]
    public void UnavailableSelectedFramebufferImageRejectsAttachmentValidation()
    {
        // Arrange: create an allocated texture but construct a descriptor that intentionally reports no storage.
        using var lifetime = StartManager();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromExternalImage(texture.TextureId, false,
            TextureDimension.Two, 1, null);

        // Act and assert: slot validation rejects the unavailable descriptor without damaging its owner.
        Assert.False(lifetime.Manager.IsResourceMetadataAvailable(image));
        Assert.Throws<InvalidOperationException>(() => image.ValidateSlot(FramebufferSlot.Color0));
        Assert.True(texture.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Installs an isolated context manager after resetting fixture-owned resource services.</summary>
    private ManagerLifetime StartManager()
    {
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        OpenGlStateCache.Current.InvalidateAll();
        return new ManagerLifetime();
    }

    /// <summary>Scopes the manager service and native binding cache used by these component cases.</summary>
    private sealed class ManagerLifetime : IDisposable
    {
        /// <summary>Creates and registers the context-scoped manager.</summary>
        internal ManagerLifetime()
        {
            Manager = new RHIResourceManager();
            RHIResourceManagerSystem.Initialize(Manager);
        }

        /// <summary>Gets the canonical metadata registry for the current case.</summary>
        internal RHIResourceManager Manager { get; }

        /// <summary>Disposes owned resources and clears the globally cached manager state.</summary>
        public void Dispose()
        {
            Manager.Dispose();
            RHIResourceManagerSystem.Shutdown();
            GL.ActiveTexture(TextureUnit.Texture0);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
