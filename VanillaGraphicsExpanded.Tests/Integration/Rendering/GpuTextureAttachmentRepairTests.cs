using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks repair of invalid attachments and managed view metadata availability.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuTextureAttachmentRepairTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Repairs invalid slots incrementally without losing pending valid-image replacement or caller bindings.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InvalidSlotsCanBeRepaired(bool managed, bool replace)
    {
        // Arrange: Make two slots invalid through a source resize while a third attached layer remains usable.
        fixture.EnsureContextValid();
        if (managed) fixture.InitializeResourceDisposal();
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var invalid = GpuFramebufferAttachment.FromTexture(source, layer: 1);
            using var valid = GpuFramebufferAttachment.FromTexture(source, layer: 0);
            using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
            using var read = DynamicGpuFramebuffer.CreateEmpty();
            using var draw = DynamicGpuFramebuffer.CreateEmpty();
            framebuffer.SetAttachment(FramebufferSlot.Color0, invalid);
            framebuffer.SetAttachment(FramebufferSlot.Color1, invalid);
            framebuffer.SetAttachment(FramebufferSlot.Color2, valid);
            Assert.True(source.Resize(8, 4, 1));
            // Act: Confirm initial bind rejection, then repair or remove invalid slots one at a time with caller FBOs bound.
            Assert.Throws<InvalidOperationException>(() => framebuffer.Bind());
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.ReadFramebuffer, read.FboId);
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.DrawFramebuffer, draw.FboId);
            // Each repair must operate independently of the still-invalid neighboring slot.
            if (replace) framebuffer.SetAttachment(FramebufferSlot.Color0, valid);
            else Assert.True(framebuffer.RemoveAttachment(FramebufferSlot.Color0));
            Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(draw.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.True(framebuffer.RemoveAttachment(FramebufferSlot.Color1));
            Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(draw.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            framebuffer.Bind();
            // Assert: The valid neighbor remains attached and the first slot reflects the selected repair strategy.
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment2,
                FramebufferParameterName.FramebufferAttachmentObjectName, out int attached);
            Assert.Equal(source.TextureId, attached);
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferParameterName.FramebufferAttachmentObjectName, out int repaired);
            Assert.Equal(replace ? source.TextureId : 0, repaired);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Unavailable managed descriptors prevent binding; recovery preserves only unchanged source storage.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ManagedUnavailableViewRejectsBind(bool own, bool storage)
    {
        // Arrange: Create a managed view, then withdraw either its own metadata or source metadata/storage publication.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
            if (own) RHIResourceManager.InvalidateWrapperMetadata(view);
            else using (new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(source, storage ? GpuMetadataChange.Storage : GpuMetadataChange.Metadata)) { }
            // Act: Attempt sampled binding while view facts are unavailable, recover the relevant metadata, then retry.
            Assert.False(view.IsValid);
            OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture2DArray, 0, 0);
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindSampledTextures([new(0, view, Dimension: TextureDimension.TwoArray)]));
            Assert.Equal(0, GL.GetInteger(GetPName.TextureBinding2DArray));
            // Reacquiring view facts leaves its allocation intact; source storage publication retires dependencies.
            if (own) RHIResourceManager.RecoverWrapperMetadata(view, new GpuTextureMetadataReader(TextureDimension.TwoArray));
            else RHIResourceManager.RecoverWrapperMetadata(source, new GpuTextureMetadataReader(TextureDimension.TwoArray));
            // Assert: Metadata-only recovery restores the view; storage publication leaves it retired and unbindable.
            Assert.Equal(!storage, view.IsValid);
            if (storage)
                Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindSampledTextures([new(0, view, Dimension: TextureDimension.TwoArray)]));
            else RHICommandContext.Current.BindSampledTextures([new(0, view, Dimension: TextureDimension.TwoArray)]);
            Assert.Equal(storage ? 0 : view.TextureId, GL.GetInteger(GetPName.TextureBinding2DArray));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
}

