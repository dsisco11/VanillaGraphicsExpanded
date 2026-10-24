using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks repair of invalid attachments and managed view metadata availability.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
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
        fixture.EnsureContextValid();
        if (managed) fixture.InitializeResourceDisposal();
        try
        {
            using var source = DynamicTexture3D.Create(4, 4, 2, PixelInternalFormat.Rgba8);
            using var invalid = GpuFramebufferAttachment.FromTexture(source, layer: 1);
            using var valid = GpuFramebufferAttachment.FromTexture(source, layer: 0);
            using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
            using var read = DynamicGpuFramebuffer.CreateEmpty();
            using var draw = DynamicGpuFramebuffer.CreateEmpty();
            framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment0, invalid);
            framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment1, invalid);
            framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment2, valid);
            if (managed) Assert.True(source.Resize(8, 4, 1));
            else Assert.Throws<AggregateException>(() => source.Resize(8, 4, 1));
            Assert.Throws<InvalidOperationException>(() => framebuffer.Bind());
            StateCache.Current.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read.FboId);
            StateCache.Current.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw.FboId);
            // Each repair must operate independently of the still-invalid neighboring slot.
            if (replace) framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment0, valid);
            else Assert.True(framebuffer.RemoveAttachment(FramebufferAttachment.ColorAttachment0));
            Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(draw.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.True(framebuffer.RemoveAttachment(FramebufferAttachment.ColorAttachment1));
            Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(draw.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            framebuffer.Bind();
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment2,
                FramebufferParameterName.FramebufferAttachmentObjectName, out int attached);
            Assert.Equal(source.TextureId, attached);
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferParameterName.FramebufferAttachmentObjectName, out int repaired);
            Assert.Equal(replace ? source.TextureId : 0, repaired);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Unavailable managed descriptors prevent binding; recovery preserves only unchanged source storage.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ManagedUnavailableViewRejectsBind(bool own, bool storage)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var source = DynamicTexture3D.Create(4, 4, 2, PixelInternalFormat.Rgba8, immutableStorage: true);
            using var view = GpuTextureView.CreateFromTexture(source, TextureTarget.Texture2DArray, PixelInternalFormat.Rgba8, 0, 1, 0, 1);
            if (own) GpuResourceManager.InvalidateWrapperMetadata(view);
            else using (storage ? StateCache.Current.BeginTextureStorageUpdate(source) : StateCache.Current.BeginTextureMetadataUpdate(source)) { }
            Assert.False(view.IsValid);
            StateCache.Current.BindTexture(TextureTarget.Texture2DArray, 0, 0);
            view.Bind(0);
            Assert.Equal(0, GL.GetInteger(GetPName.TextureBinding2DArray));
            // Reacquiring view facts leaves its allocation intact; source storage publication retires dependencies.
            if (own) GpuResourceManager.ObserveWrapperMetadata(view, new GpuTextureMetadataReader(TextureTarget.Texture2DArray), GpuMetadataChange.Metadata);
            else source.SetMipRange(0, 0);
            Assert.Equal(!storage, view.IsValid);
            view.Bind(0);
            Assert.Equal(storage ? 0 : view.TextureId, GL.GetInteger(GetPName.TextureBinding2DArray));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
}

