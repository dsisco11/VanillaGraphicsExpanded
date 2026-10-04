using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Validates borrowed scene storage through actual texture allocations and publication metadata.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorFrameTargetsTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>HDR storage accepts floating RGBA while rejecting normalized and integer scene images.</summary>
    [Theory]
    [InlineData(PixelInternalFormat.Rgba16f, true)]
    [InlineData(PixelInternalFormat.Rgba32f, true)]
    [InlineData(PixelInternalFormat.Rgba8, false)]
    [InlineData(PixelInternalFormat.Rgba32ui, false)]
    public void RequiresFloatingColorStorage(PixelInternalFormat format, bool expected)
    {
        EnsureContextValid();
        using var image = DynamicTexture2D.Create(2, 2, format);
        var frames = CreatePublications(image);
        var targets = new SceneColorFrameTargets();
        Assert.Equal(expected, targets.Prepare(frames));
        Assert.Equal(expected, targets.Prepare(frames));
        targets.Invalidate();
        Assert.True(image.IsValid);
    }

    /// <summary>Missing attachments and dimensions reject a frame; an explicit invalidation reimports resized borrowed storage.</summary>
    [Fact]
    public void MissingAndResizedPublicationsRequireCurrentMetadata()
    {
        EnsureContextValid();
        using var image = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba16f);
        var frames = CreatePublications(image);
        var targets = new SceneColorFrameTargets();
        Assert.True(targets.Prepare(frames));
        var primary = frames[(int)EnumFrameBuffer.Primary];
        frames[(int)EnumFrameBuffer.Primary] = null!;
        Assert.False(targets.Prepare(frames));
        frames[(int)EnumFrameBuffer.Primary] = primary;
        primary.ColorTextureIds = [];
        Assert.False(targets.Prepare(frames));
        primary.ColorTextureIds = [image.TextureId];
        primary.Width = 0;
        Assert.False(targets.Prepare(frames));
        primary.Width = 2;

        // Engine publication follows the actual resize, and the owner explicitly
        // withdraws cached metadata before accepting that new generation.
        image.Resize(3, 3);
        targets.Invalidate();
        Assert.False(targets.Prepare(frames));
        foreach (var frame in frames) { frame.Width = 3; frame.Height = 3; }
        Assert.True(targets.Prepare(frames));
        Assert.True(image.IsValid);
    }
    #endregion

    #region Private
    /// <summary>Provides a complete engine slot list sharing one image so tests isolate storage validation.</summary>
    private static List<FrameBufferRef> CreatePublications(DynamicTexture2D image)
        => Enumerable.Range(0, Enum.GetValues<EnumFrameBuffer>().Max(value => (int)value) + 1)
            .Select(_ => new FrameBufferRef
            {
                Width = image.Width, Height = image.Height, ColorTextureIds = [image.TextureId]
            }).ToList();
    #endregion
}
