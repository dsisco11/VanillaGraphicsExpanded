using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks immutable source lifetime independently of the selected lighting implementation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionLifecycleTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Repeated toggles invalidate publication, resize both images together and preserve borrowed color.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceLifetimeSurvivesTogglesResizeAndReload(bool lumon)
    {
        EnsureContextValid();
        using var source = new WaterRefractionScene();
        using var composite = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba16f);
        using var resized = DynamicTexture2D.Create(3, 1, PixelInternalFormat.Rgba16f);
        var config = new VanillaGraphicsExpanded.LumOn.VgeConfig();
        config.LumOn.Enabled = lumon;
        Assert.Null(source.BeginFrame(config.WaterRefractionEnabled, composite));
        Assert.Null(source.Color);
        Assert.Null(source.Depth);
        for (int transition = 0; transition < 3; transition++)
        {
            config.WaterRefractionEnabled = true;
            var target = source.BeginFrame(config.WaterRefractionEnabled, composite)!;
            Assert.False(source.Published);
            Assert.Same(composite, target[0]);
            Assert.NotSame(composite, source.Color);
            Assert.NotEqual(composite.TextureId, source.Color!.TextureId);
            source.Color.UploadDataImmediate(Enumerable.Repeat(.5f, 16).ToArray());
            source.Depth!.UploadDataImmediate(Enumerable.Repeat(.75f, 4).ToArray());
            source.Publish();
            Assert.True(source.Published);
            composite.UploadDataImmediate(Enumerable.Repeat(2f, 16).ToArray());
            Assert.All(source.Color.ReadPixels(), value => Assert.Equal(.5f, value));
            // A new frame must withdraw last frame's publication before any consumer can use it.
            Assert.Same(target, source.BeginFrame(true, composite));
            Assert.False(source.Published);
            source.Publish();
            config.LumOn.Enabled = !config.LumOn.Enabled;
            Assert.Same(target, source.BeginFrame(true, composite));
            Assert.False(source.Published);
            var beforeResize = source.Color;
            Assert.True(composite.Resize(4, 2));
            source.BeginFrame(true, composite);
            Assert.False(beforeResize!.IsValid);
            Assert.Equal(4, source.Color!.Width);
            Assert.True(composite.Resize(2, 2));
            source.BeginFrame(true, composite);
            var oldColor = source.Color;
            var oldDepth = source.Depth;
            source.BeginFrame(true, resized);
            Assert.False(oldColor!.IsValid);
            Assert.False(oldDepth!.IsValid);
            Assert.Equal(3, source.Color!.Width);
            Assert.Equal(1, source.Depth!.Height);
            source.Publish();
            source.Invalidate();
            Assert.False(source.Published);
            config.WaterRefractionEnabled = false;
            Assert.Null(source.BeginFrame(config.WaterRefractionEnabled, resized));
            Assert.Null(source.Color);
            Assert.Null(source.Depth);
            Assert.True(composite.IsValid);
            Assert.True(resized.IsValid);
        }
        source.Dispose();
        Assert.False(source.Published);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
