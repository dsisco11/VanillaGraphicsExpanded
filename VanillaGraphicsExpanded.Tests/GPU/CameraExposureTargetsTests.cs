using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks temporal exposure publication never aliases its sampled history and retires owned storage.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CameraExposureTargetsTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Only successful publication swaps stable images; disposing the owner deletes all owned textures.</summary>
    [Fact]
    public void PublicationSwapsDistinctPersistentHistoryImages()
    {
        EnsureContextValid();
        var targets = new CameraExposureTargets();
        int histogram = targets.Histogram.TextureId;
        int original = targets.Exposure.TextureId;
        int pending = targets.WriteTarget[0].TextureId;
        try
        {
            Assert.NotEqual(original, pending);
            Assert.Equal(64, targets.HistogramTarget.Width);
            Assert.Equal(1, targets.HistogramTarget.Height);
            targets.WriteTarget.BindWithViewport();
            GL.ClearBuffer(ClearBuffer.Color, 0, new[] { 2f, 0f, 0f, 0f });
            Assert.Equal(original, targets.Exposure.TextureId);
            targets.Publish();
            Assert.Equal(pending, targets.Exposure);
            Assert.Equal(original, targets.WriteTarget[0].TextureId);
            Assert.Equal(histogram, targets.Histogram.TextureId);
            targets.Publish();
            Assert.Equal(original, targets.Exposure);
            Assert.Equal(pending, targets.WriteTarget[0].TextureId);
            AssertNoGLError("exposure history publication");
        }
        finally { targets.Dispose(); }
        Assert.False(GL.IsTexture(histogram));
        Assert.False(GL.IsTexture(original));
        Assert.False(GL.IsTexture(pending));
    }
    #endregion
}
