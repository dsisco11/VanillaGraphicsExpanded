using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies imported framebuffer images preserve their layer and cube-face selections during copies.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class FramebufferAttachmentDiscoveryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Configured copies reject retired borrowed color or depth storage before issuing driver work.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfiguredBlitterRejectsRetiredBackingStorage(bool retireDepth)
    {
        // Arrange
        EnsureContextValid();
        using var color = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depth = new DepthTexture(2, 2, TextureFormat.DepthComponent24);
        using var source = DynamicGpuFramebuffer.CreateSingle(color, depth)!;
        using var destination = CreateRenderTarget(2, 2, TextureFormat.Rgba32f);
        using var destinationDepth = new DepthTexture(2,2,TextureFormat.DepthComponent24);
        destination.Attach(destinationDepth);
        var copy = FramebufferTestSetup.DescribeCopy(source, destination, retireDepth ? ImageAspects.Depth : ImageAspects.Color);
        if (retireDepth) depth.Dispose();
        else color.Dispose();
        // Act
        Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BlitImages(copy));

        // Assert
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Covered aliases retain observed ordinary attachment provenance and copy the selected array layer.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExternalArrayLayerCopiesSelectedImage(int layer)
    {
        // Arrange
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var scope = OpenGlStateCache.Current.BindFramebufferScope();
            using var texture = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(texture, layer: layer);
            using var source = DynamicGpuFramebuffer.Create([image])!;
            source.Bind();
            GL.ClearBuffer(ClearBuffer.Color, 0, new[] { 0.25f, 0.5f, 0.75f, 1f });
            using var external = DynamicGpuFramebuffer.Wrap(source.FboId, width: 2, height: 2);
            using var output = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var destination = DynamicGpuFramebuffer.CreateSingle(output)!;
            // Act: discover the wrapped attachment and copy the image selected by its recorded layer.
            var imported = GpuFramebufferAttachmentDiscovery.Read(external, FramebufferSlot.Color0);
            using var importedTexture = new BorrowedTexture(imported.TextureId);
            int selection = imported.Layer ?? (imported.CubeFace is { } face ? (int)face-(int)CubeFace.PositiveX : 0);
            var copy = new RHIImageBlit(RHIAttachment.FromTexture(importedTexture,new(imported.MipLevel,selection)),
                new(0,0,2,2),FramebufferTestSetup.Select(destination.GetAttachment(FramebufferSlot.Color0)!),new(0,0,2,2));
            RHICommandContext.Current.BlitImages(copy);
            // Assert
            Assert.Equal(new[] { 0.25f, 0.5f, 0.75f, 1f }, destination[0].ReadTexel()[..4]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A context-local raw alias cannot invent ordinary replay provenance from ambiguous array attachment queries.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LocalArrayAliasImportsSelectedImage(int layer)
    {
        // Arrange
        EnsureContextValid();
        Assert.SkipUnless(GpuSupport.Graphics.SupportsOvrMultiview, "OVR multiview support is required to validate the local framebuffer alias.");
        fixture.InitializeResourceDisposal();
        using var texture = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(texture, layer: layer);
        using var source = DynamicGpuFramebuffer.Create([image]);
        using var external = DynamicGpuFramebuffer.Wrap(source.FboId, width: 2, height: 2);
        using var output = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var destination = DynamicGpuFramebuffer.CreateSingle(output)!;
        // Act
        var imported = GpuFramebufferAttachmentDiscovery.Read(external, FramebufferSlot.Color0);

        // Assert
        Assert.Equal(texture.TextureId, imported.TextureId);
        Assert.Equal(layer, imported.Layer);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>External cube-map attachments import the selected face without treating it as a 2D texture.</summary>
    [Fact]
    public void ExternalCubeFaceCopiesSelectedImage()
    {
        // Arrange
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        int texture = GL.GenTexture();
        try
        {
            // No cube-map allocation wrapper exists; allocate every face to provide complete cube storage.
            using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.TextureCubeMap, 0, texture))
            {
                for (int face = 0; face < 6; face++)
                    GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, 0, PixelInternalFormat.Rgba32f,
                        2, 2, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            }
            using var source = DynamicGpuFramebuffer.CreateEmpty();
            source.Bind();
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.TextureCubeMapNegativeZ, texture, 0);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0); OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
            GL.ClearBuffer(ClearBuffer.Color, 0, new[] { 0.125f, 0.25f, 0.5f, 1f });
            using var external = DynamicGpuFramebuffer.Wrap(source.FboId, width: 2, height: 2);
            using var destination = CreateRenderTarget(2, 2, TextureFormat.Rgba32f);
            // Act: discover the external cube face and copy that selected face.
            var imported = GpuFramebufferAttachmentDiscovery.Read(external, FramebufferSlot.Color0);
            using var borrowedTexture = new BorrowedTexture(imported.TextureId);
            var copy = new RHIImageBlit(RHIAttachment.FromTexture(borrowedTexture, new(imported.MipLevel, (int)imported.CubeFace!.Value - (int)CubeFace.PositiveX)),
                new(0,0,2,2),FramebufferTestSetup.Select(destination.GetAttachment(FramebufferSlot.Color0)!),new(0,0,2,2));
            RHICommandContext.Current.BlitImages(copy);

            // Assert
            Assert.Equal(new[] { 0.125f, 0.25f, 0.5f, 1f }, destination[0].ReadTexel()[..4]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, texture));
        }
    }
    #endregion
}
