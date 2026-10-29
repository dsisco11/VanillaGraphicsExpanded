using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks named metadata reads and required cube-face discovery without disturbing surrounding selectors.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuMetadataBoundAcquisitionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Named buffer, texture and framebuffer queries preserve independent sentinel bindings.</summary>
    [Fact]
    public void MetadataAcquisitionPreservesBindings()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            using var sentinelBuffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            buffer.Allocate(96);
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var sentinelTexture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            using var sentinelFramebuffer = DynamicGpuFramebuffer.CreateEmpty();
            using var bufferBinding = OpenGlStateCache.Current.BindBufferScope(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBufferValues.Target(sentinelBuffer.Role), sentinelBuffer.BufferId);
            using var textureBinding = OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)sentinelTexture).Target), 0, sentinelTexture.TextureId);
            using var framebufferBinding = OpenGlStateCache.Current.BindFramebufferScope(FramebufferTarget.Framebuffer, sentinelFramebuffer.FboId);
            Assert.True(new GpuBufferMetadataReader().TryRead(buffer.BufferId, out var bufferMetadata));
            Assert.Equal((nint)96, bufferMetadata.Capacity);
            Assert.True(new GpuTextureMetadataReader(TextureDimension.Two).TryRead(texture.TextureId, out var textureMetadata));
            Assert.Equal(TextureDimension.Two, textureMetadata.Target);
            Assert.True(GpuFramebufferMetadataReader.Instance.TryRead(framebuffer.FboId, out var framebufferMetadata));
            Assert.Equal(texture.TextureId, framebufferMetadata.Attachments[FramebufferSlot.Color0].Id);
            Assert.Equal(sentinelBuffer.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
            Assert.Equal(sentinelTexture.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(sentinelFramebuffer.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(sentinelFramebuffer.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>The single buffer-texture factory preserves explicit whole/range intent without temporary binding.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BufferTextureFactoryPreservesDescriptors(bool ranged)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            int offset = Math.Max(1, GpuSupport.Graphics.TextureBufferOffsetAlignment);
            buffer.Allocate(offset + 512);
            using var sentinel = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
            using var sentinelBinding = OpenGlStateCache.Current.BindTextureScope(TextureTarget.TextureBuffer, 0, sentinel.TextureId);
            using GpuBufferView view = ranged
                ? RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = false, Offset = offset, Length = 256, Label = null }).RequireResource()
                : RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
            Assert.Equal(buffer.BufferId, view.BufferId);
            Assert.Equal((nint)(ranged ? offset : 0), view.OffsetBytes);
            Assert.Equal((nint)(ranged ? 256 : 0), view.SizeBytes);
            Assert.Equal(TextureFormat.R32f, view.Format);
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBindingBuffer));
            var entry = GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(view)!;
            Assert.True(entry.MetadataAvailable);
            Assert.Equal((ulong)1, entry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Cube-face discovery uses its required bound selection and restores the incoming unit and cube binding.</summary>
    [Fact]
    public void CubeFaceDiscoveryPreservesSelectedUnitAndCubeBinding()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var cube = context.CreateTextureCube(new RHITextureDescription
            { Dimension = TextureDimension.Cube, Format = TextureFormat.Rgba8, Width = 4, Height = 4,
                MipLevels = 2, Storage = ResourceStorageMode.Immutable }).RequireResource();
            using var sentinel = context.CreateTextureCube(new RHITextureDescription
            { Dimension = TextureDimension.Cube, Format = TextureFormat.Rgba8, Width = 2, Height = 2,
                MipLevels = 1, Storage = ResourceStorageMode.Immutable }).RequireResource();
            using var selection = OpenGlStateCache.Current.BindTextureScope(TextureTarget.TextureCubeMap, 3, sentinel.TextureId);
            GL.ActiveTexture(TextureUnit.Texture3);

            Assert.True(new GpuTextureMetadataReader(TextureDimension.Cube).TryRead(cube.TextureId, out var snapshot));
            Assert.Equal(TextureDimension.Cube, snapshot.Target);
            Assert.Equal(12, snapshot.Levels.Count(image => image.Allocated));
            Assert.All(snapshot.Levels.Where(image => image.Allocated), image =>
            {
                Assert.NotEqual(CubeFace.None, image.Face);
                Assert.Equal(image.Level == 0 ? 4 : 2, image.Width);
                Assert.Equal(image.Width, image.Height);
            });
            Assert.Equal((int)TextureUnit.Texture3, GL.GetInteger(GetPName.ActiveTexture));
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBindingCubeMap));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
}
