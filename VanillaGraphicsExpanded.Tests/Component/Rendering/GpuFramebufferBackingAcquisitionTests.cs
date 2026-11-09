using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies framebuffer backing acquisition rejects resources while storage mutation is unfinished.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuFramebufferBackingAcquisitionTests(HeadlessGLFixture fixture)
{
    #region Public API

    /// <summary>Backing acquisition rejects a texture or renderbuffer while its explicit mutation is unfinished.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackingAcquisitionRejectsUnfinishedOperation(bool renderbuffer)
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        // Act
        try
        {
            if (renderbuffer)
            {
                using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
                using var operation = new RHIResourceManager.MetadataOperation<GpuRenderbufferMetadata>(owner, GpuMetadataChange.Storage);
                // Assert
                Assert.Throws<InvalidOperationException>(() => RHIResourceManager.AcquireFramebufferRenderbuffer(owner.RenderbufferId));
            }
            else
            {
                using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
                using var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(owner, GpuMetadataChange.Storage);
                Assert.Throws<InvalidOperationException>(() => RHIResourceManager.AcquireFramebufferTexture(owner.TextureId, TextureDimension.Two));
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
}

