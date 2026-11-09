using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using Xunit;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks component projections from manager-owned canonical texture facts.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuImageMetadataProjectionRegressionTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Managed image projection

    /// <summary>A managed texture view projects its selected image without rereading its canonical metadata.</summary>
    [Fact]
    public void ManagedViewAttachmentReusesCanonicalSnapshot()
    {
        // Arrange: create a managed view, then make any unexpected native metadata read fail immediately.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription
            {
                Dimension = TextureDimension.TwoArray,
                Format = TextureFormat.Rgba32f,
                Width = System.Math.Max(1, 8),
                Height = System.Math.Max(1, 4),
                Depth = System.Math.Max(1, 6),
                MipLevels = 1,
                Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable),
                Filter = TextureFilterMode.Linear,
                BaseLevel = 0,
                MaxLevel = (1) - 1,
                Label = null
            }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription
            {
                Source = texture,
                Dimension = TextureDimension.TwoArray,
                Format = TextureFormat.Rgba32f,
                BaseMip = 0,
                MipCount = 1,
                BaseLayer = 2,
                LayerCount = 3,
                Label = null
            }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(view));
            var original = entry.Reader;

            try
            {
                entry.Reader = new RejectNativeRead();

                // Act: project the selected layer through the managed framebuffer attachment component.
                using var image = GpuFramebufferAttachment.FromTexture(view, layer: 1);

                // Assert: projection reuses the same canonical entry and selected dimensions.
                Assert.Same(entry, manager.GetResourceRegistration(image));
                Assert.Equal(8, image.Width);
                Assert.Equal(4, image.Height);
                Assert.Equal(1, image.ImageDepth);
                Assert.True(entry.MetadataAvailable);
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
            finally
            {
                entry.Reader = original;
            }
        }
        finally
        {
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion
    #region Cube image projection

    /// <summary>A selected external cube face retains the canonical image metadata and face identity.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalCubeFaceProjectsCanonicalImage(bool dsa)
    {
        // Arrange: create the cube storage under both supported direct-state-access capability modes.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities = new DirectStateAccessCapabilityScope(dsa);
            using var cube = RHICommandContext.Current.CreateTextureCube(new RHITextureDescription
            {
                Dimension = TextureDimension.Cube,
                Format = TextureFormat.Rgba8,
                Width = System.Math.Max(1, 8),
                Height = System.Math.Max(1, 8),
                MipLevels = 2,
                Storage = ResourceStorageMode.Mutable,
                Filter = TextureFilterMode.Nearest,
                BaseLevel = 0,
                MaxLevel = (2) - 1,
                Label = null
            }).RequireResource();

            // Act: project the negative-Z face at the selected nonbase mip through the component.
            using var image = GpuFramebufferAttachment.FromTextureId(cube.TextureId, mipLevel: 1, cubeFace: CubeFace.NegativeZ);

            // Assert: dimensions, format, face identity, and canonical manager ownership are preserved.
            Assert.Equal(4, image.Width);
            Assert.Equal(4, image.Height);
            Assert.Equal(CubeFace.NegativeZ, image.CubeFace);
            Assert.Equal(TextureFormat.Rgba8, image.InternalFormat);
            Assert.Same(RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(cube),
                RHIResourceManagerSystem.CurrentManager.GetResourceRegistration(image));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion
    #endregion

    #region Private

    /// <summary>Fails if managed projection attempts another native texture acquisition.</summary>
    private sealed class RejectNativeRead : IGpuResourceMetadataReader<GpuTextureMetadata>
    {
        #region Public API

        /// <inheritdoc />
        public bool IsResource(nint id) => throw new InvalidOperationException("Projection must reuse canonical facts.");

        /// <inheritdoc />
        public bool TryRead(nint id, out GpuTextureMetadata metadata) => throw new InvalidOperationException("Projection must reuse canonical facts.");

        #endregion
    }

    #endregion
}
