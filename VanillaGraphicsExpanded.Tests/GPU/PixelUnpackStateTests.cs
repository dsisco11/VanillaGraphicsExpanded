using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using UnpackState = VanillaGraphicsExpanded.Rendering.StateCache.PixelUnpackState;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies complete unpack-layout borrowing and streaming uploads against driver state.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PixelUnpackStateTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Nested scopes restore all eight fields after successful and exceptional operations.</summary>
    [Fact]
    public void NestedScopesRestoreAllFields()
    {
        EnsureContextValid();
        var cache = StateCache.Current;
        var incoming = new UnpackState(8, 19, 3, 7, true, true, 23, 2);
        using var restore = cache.SetPixelUnpackScope(incoming);
        using (cache.SetPixelUnpackScope(new(1)))
        {
            AssertState(new(1));
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var inner = cache.SetPixelUnpackScope(new(2, 11, 1, 4, true, true, 17, 3));
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
        EnsureContextValid();
        var cache = StateCache.Current;
        using var restore = cache.SetPixelUnpackScope(new(1));
        if (floatingPoint)
        {
            cache.SetPixelStore(PixelStoreParameter.UnpackImageHeight, 17f);
            cache.SetPixelStore(PixelStoreParameter.UnpackSkipImages, 3f);
        }
        else
        {
            cache.SetPixelStore(PixelStoreParameter.UnpackImageHeight, 17);
            cache.SetPixelStore(PixelStoreParameter.UnpackSkipImages, 3);
        }
        AssertState(new(1, ImageHeight: 17, SkipImages: 3));
    }

    /// <summary>Invalid image dimensions are rejected before changing any native layout field.</summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void NegativeImageLayoutIsRejected(int imageHeight, int skipImages)
    {
        EnsureContextValid();
        using var restore = StateCache.Current.SetPixelUnpackScope(new(8, ImageHeight: 13, SkipImages: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => StateCache.Current.SetPixelUnpackState(new(1, ImageHeight: imageHeight, SkipImages: skipImages)));
        AssertState(new(8, ImageHeight: 13, SkipImages: 2));
    }

    /// <summary>Padded array uploads use their declared layout and preserve the caller's complete unpack state.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamingArrayUploadPreservesIncomingLayout(bool pbo)
    {
        EnsureContextValid();
        var cache = StateCache.Current;
        int texture = GL.GenTexture();
        cache.BindTexture(TextureTarget.Texture2DArray, 0, texture);
        GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba8, 1, 1, 2, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        using var manager = new TextureStreamingManager(TextureStreamingSettings.Default with
        { EnablePboStreaming = pbo, ForceDisablePersistent = true, MaxFrameBudgetMs = 0 });
        var incoming = new UnpackState(8, 19, 3, 7, true, true, 23, 2);
        using var restore = cache.SetPixelUnpackScope(incoming);
        try
        {
            // Each source image occupies two rows of two RGBA pixels; only its first pixel is uploaded.
            byte[] source = new byte[32];
            byte[] first = [11, 22, 33, 255];
            byte[] second = [44, 55, 66, 255];
            first.CopyTo(source, 0); second.CopyTo(source, 16);
            manager.Enqueue(new(texture, TextureUploadTarget.For2DArray(), TextureUploadRegion.For2DArray(0, 0, 0, 1, 1, 2),
                PixelFormat.Rgba, PixelType.UnsignedByte, TextureUploadData.From(source), UnpackRowLength: 2, UnpackImageHeight: 2));
            manager.TickOnRenderThread();
            Assert.Equal(1, manager.GetDiagnosticsSnapshot().Uploaded);
            Assert.Equal(pbo ? 0 : 1, manager.GetDiagnosticsSnapshot().FallbackUploads);
            if (pbo) Assert.Equal(TextureStreamingBackendKind.TripleBuffered, manager.GetDiagnosticsSnapshot().Backend);
            AssertState(incoming);
            byte[] actual = new byte[8];
            using (cache.SetPixelPackScope(new(1)))
            {
                cache.BindTexture(TextureTarget.Texture2DArray, 0, texture);
                GL.GetTexImage(TextureTarget.Texture2DArray, 0, PixelFormat.Rgba, PixelType.UnsignedByte, actual);
            }
            Assert.Equal(first.Concat(second).ToArray(), actual);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { cache.BindTexture(TextureTarget.Texture2DArray, 0, 0); GL.DeleteTexture(texture); }
    }
    #endregion

    #region Private
    /// <summary>Compares the cached layout with independently queried driver values.</summary>
    private static void AssertState(UnpackState expected)
    {
        Assert.Equal(expected, StateCache.Current.GetPixelUnpackState());
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
