using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using UnpackState = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGlStateCache.PixelUnpackState;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies complete unpack-layout borrowing and streaming uploads against driver state.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PixelUnpackStateTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Nested scopes restore all eight fields after successful and exceptional operations.</summary>
    [Fact]
    public void NestedScopesRestoreAllFields()
    {
        // Arrange: Seed all eight unpack fields and nest borrowed layout scopes.
        // Act: Change the layout and throw from the inner operation.
        // Assert: Every driver and cached field is restored on unwind.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        var incoming = new UnpackState(8, 19, 3, 7, true, true, 23, 2);
        using var restore = OpenGLPixelLayouts.UnpackScope(cache, incoming);
        using (OpenGLPixelLayouts.UnpackScope(cache, new(1)))
        {
            AssertState(new(1));
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var inner = OpenGLPixelLayouts.UnpackScope(cache, new(2, 11, 1, 4, true, true, 17, 3));
                AssertState(new(2, 11, 1, 4, true, true, 17, 3));
                throw new InvalidOperationException("Controlled upload failure");
            }));
            AssertState(new(1));
        }
        AssertState(incoming);
    }

    /// <summary>Integer and float pixel-store adapters invalidate cached image-stride fields.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImageStrideWritesInvalidateLayout(bool floatingPoint)
    {
        // Arrange: Cache the current unpack layout and select integer or float image-stride entrypoints.
        // Act: Write image height/skip fields through the selected adapter.
        // Assert: Cached stride knowledge is invalidated for later acquisition.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        using var restore = OpenGLPixelLayouts.UnpackScope(cache, new(1));
        if (floatingPoint)
        {
            GL.PixelStore(PixelStoreParameter.UnpackImageHeight, 17f);
            GL.PixelStore(PixelStoreParameter.UnpackSkipImages, 3f);
        }
        else
        {
            GL.PixelStore(PixelStoreParameter.UnpackImageHeight, 17);
            GL.PixelStore(PixelStoreParameter.UnpackSkipImages, 3);
        }
        AssertState(new(1, ImageHeight: 17, SkipImages: 3));
    }

    /// <summary>Invalid image dimensions are rejected before changing any native layout field.</summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void NegativeImageLayoutIsRejected(int imageHeight, int skipImages)
    {
        // Arrange: Record all unpack state before using negative image dimensions.
        // Act: Submit the invalid image layout through the native API.
        // Assert: Rejection leaves every native layout field unchanged.
        EnsureContextValid();
        using var restore = OpenGLPixelLayouts.UnpackScope(OpenGlStateCache.Current, new(8, ImageHeight: 13, SkipImages: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => OpenGLPixelLayouts.SetUnpack(OpenGlStateCache.Current, new(1, ImageHeight: imageHeight, SkipImages: skipImages)));
        AssertState(new(8, ImageHeight: 13, SkipImages: 2));
    }

    /// <summary>Padded array uploads use their declared layout and preserve the caller's complete unpack state.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamingArrayUploadPreservesIncomingLayout(bool pbo)
    {
        // Arrange: Seed a padded unpack layout and optionally bind a pixel-unpack buffer.
        // Act: Upload an array using its declared row/image strides.
        // Assert: Uploaded pixels match their source and all incoming unpack state is restored.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        int texture = GL.GenTexture();
        OpenGLImageBindings.BindTexture(cache, TextureTarget.Texture2DArray, 0, texture);
        GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba8, 1, 1, 2, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        using var manager = new TextureStreamingManager(TextureStreamingSettings.Default with
        { EnablePboStreaming = pbo, ForceDisablePersistent = true, MaxFrameBudgetMs = 0 });
        var incoming = new UnpackState(8, 19, 3, 7, true, true, 23, 2);
        using var restore = OpenGLPixelLayouts.UnpackScope(cache, incoming);
        try
        {
            // Each source image occupies two rows of two RGBA pixels; only its first pixel is uploaded.
            byte[] source = new byte[32];
            byte[] first = [11, 22, 33, 255];
            byte[] second = [44, 55, 66, 255];
            first.CopyTo(source, 0); second.CopyTo(source, 16);
            manager.Enqueue(new(texture, TextureUploadTarget.For2DArray(), TextureUploadRegion.For2DArray(0, 0, 0, 1, 1, 2),
                PixelChannelLayout.Rgba, PixelComponentType.UnsignedByte, TextureUploadData.From(source), UnpackRowLength: 2, UnpackImageHeight: 2));
            manager.TickOnRenderThread();
#if DEBUG
            Assert.Equal(1, manager.GetDiagnosticsSnapshot().Uploaded);
            Assert.Equal(pbo ? 0 : 1, manager.GetDiagnosticsSnapshot().FallbackUploads);
#endif
            if (pbo) Assert.Equal(TextureStreamingBackendKind.TripleBuffered, manager.Backend);
            AssertState(incoming);
            byte[] actual = new byte[8];
            using (OpenGLPixelLayouts.PackScope(cache, new(1)))
            {
                OpenGLImageBindings.BindTexture(cache, TextureTarget.Texture2DArray, 0, texture);
                GL.GetTexImage(TextureTarget.Texture2DArray, 0, PixelFormat.Rgba, PixelType.UnsignedByte, actual);
            }
            Assert.Equal(first.Concat(second).ToArray(), actual);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { OpenGLImageBindings.BindTexture(cache, TextureTarget.Texture2DArray, 0, 0); GL.DeleteTexture(texture); }
    }
    #endregion

    #region Private
    /// <summary>Compares the cached layout with independently queried driver values.</summary>
    private static void AssertState(UnpackState expected)
    {
        Assert.Equal(expected, OpenGLPixelLayouts.GetUnpack(OpenGlStateCache.Current));
        Assert.Equal(expected.Alignment, GL.GetInteger(GetPName.UnpackAlignment));
        Assert.Equal(expected.RowLength, GL.GetInteger(GetPName.UnpackRowLength));
        Assert.Equal(expected.SkipRows, GL.GetInteger(GetPName.UnpackSkipRows));
        Assert.Equal(expected.SkipPixels, GL.GetInteger(GetPName.UnpackSkipPixels));
        Assert.Equal(expected.SwapBytes, GL.GetInteger(GetPName.UnpackSwapBytes) != 0);
        Assert.Equal(expected.LsbFirst, GL.GetInteger(GetPName.UnpackLsbFirst) != 0);
        Assert.Equal(expected.ImageHeight, GL.GetInteger(GetPName.UnpackImageHeight));
        Assert.Equal(expected.SkipImages, GL.GetInteger(GetPName.UnpackSkipImages));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
