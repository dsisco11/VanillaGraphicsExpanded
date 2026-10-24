using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises capability-selected bound metadata acquisition on the available native context.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuMetadataBoundAcquisitionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Bound buffer, texture, and framebuffer queries preserve sentinel bindings and publish native facts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataAcquisitionPreservesBindings(bool dsa)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities = new DirectStateAccessCapabilityScope(dsa);
            using var buffer = GpuVbo.Create();
            using var sentinelBuffer = GpuVbo.Create();
            buffer.Allocate(96);
            using var texture = DynamicTexture2D.Create(8, 4, PixelInternalFormat.Rgba8);
            using var sentinelTexture = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            using var sentinelFramebuffer = DynamicGpuFramebuffer.CreateEmpty();
            using var bufferBinding = sentinelBuffer.BindScope();
            using var textureBinding = sentinelTexture.BindScope(0);
            using var framebufferBinding = StateCache.Current.BindFramebufferScope(FramebufferTarget.Framebuffer, sentinelFramebuffer.FboId);
            Assert.True(new GpuBufferMetadataReader().TryRead(buffer.BufferId, out var bufferMetadata));
            Assert.Equal((nint)96, bufferMetadata.Capacity);
            Assert.True(new GpuTextureMetadataReader(TextureTarget.Texture2D).TryRead(texture.TextureId, out var textureMetadata));
            Assert.Equal(TextureTarget.Texture2D, textureMetadata.Target);
            Assert.True(GpuFramebufferMetadataReader.Instance.TryRead(framebuffer.FboId, out var framebufferMetadata));
            Assert.Equal(texture.TextureId, framebufferMetadata.Attachments[FramebufferAttachment.ColorAttachment0].Id);
            Assert.Equal(sentinelBuffer.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
            Assert.Equal(sentinelTexture.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(sentinelFramebuffer.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(sentinelFramebuffer.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Both public buffer-texture factories preserve whole/range descriptors through named and bound setup.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void BufferTextureFactoriesPreserveDescriptors(bool dsa, bool preferredType, bool ranged)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities = new DirectStateAccessCapabilityScope(dsa);
            using var buffer = GpuVbo.Create();
            int offset = Math.Max(1, GpuSupport.Graphics.TextureBufferOffsetAlignment);
            buffer.Allocate(offset + 512);
            using var sentinel = GpuBufferView.CreateWholeBuffer(buffer.BufferId, SizedInternalFormat.R32f);
            using var sentinelBinding = StateCache.Current.BindTextureScope(TextureTarget.TextureBuffer, 0, sentinel.TextureId);
            using GpuBufferView view = preferredType
                ? ranged ? GpuBufferTexture.CreateRange(buffer.BufferId, SizedInternalFormat.R32f, offset, 256)
                    : GpuBufferTexture.CreateWholeBuffer(buffer.BufferId, SizedInternalFormat.R32f)
                : ranged ? GpuBufferView.CreateRange(buffer.BufferId, SizedInternalFormat.R32f, offset, 256)
                    : GpuBufferView.CreateWholeBuffer(buffer.BufferId, SizedInternalFormat.R32f);
            Assert.Equal(buffer.BufferId, view.BufferId);
            Assert.Equal((nint)(ranged ? offset : 0), view.OffsetBytes);
            Assert.Equal((nint)(ranged ? 256 : 0), view.SizeBytes);
            Assert.Equal(SizedInternalFormat.R32f, view.Format);
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBindingBuffer));
            var entry = GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(view)!;
            Assert.True(entry.MetadataAvailable);
            Assert.Equal((ulong)1, entry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
}
