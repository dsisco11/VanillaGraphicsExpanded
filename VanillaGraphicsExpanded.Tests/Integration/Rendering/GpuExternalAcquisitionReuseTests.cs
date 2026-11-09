using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies external aliases reuse covered facts and local images project supplied owner snapshots.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuExternalAcquisitionReuseTests(HeadlessGLFixture fixture)
{
    #region Public API

    /// <summary>Unknown and withdrawn native storage recovers coherent facts across image aliases.</summary>
    [Fact]
    public void UnknownAndUnavailableImagesRecoverThenReuse()
    {
        // Arrange raw native storage unknown to the manager before creating image aliases.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int native = GL.GenTexture();
        try
        {
            // Act by allocating storage directly, then acquire two aliases over the same native name.
            using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, native))
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            using var first = GpuFramebufferAttachment.FromTextureId(native);
            using var second = GpuFramebufferAttachment.FromTextureId(native);
            Assert.Equal(first.Width, second.Width);
            Assert.Same(RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(first),
                RHIResourceManagerSystem.CurrentManager.GetResourceRegistration(second));
            using var borrowed = new BorrowedTexture(native);
            // Assert aliases share the original registration; then change storage externally to invalidate it.
            using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, native))
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 12, 6, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            using var reads = new ImageDescriptorCapture();
            // Act by acquiring after the external storage update, then acquire again to check stable reuse.
            using var recovered = GpuFramebufferAttachment.FromTextureId(native);
            // Assert the first acquisition sees updated dimensions and the next one reuses those facts.
            Assert.Equal(12, recovered.Width);
            Assert.Equal(6, first.Height);
            using var stable = GpuFramebufferAttachment.FromTextureId(native);
            Assert.Equal(recovered.Width, stable.Width);
            Assert.Equal(recovered.Height, stable.Height);
            Assert.Equal(0, reads.Reads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, native)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
}
