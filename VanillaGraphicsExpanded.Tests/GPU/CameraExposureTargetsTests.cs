using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks temporal exposure publication never aliases its sampled history and retires owned storage.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CameraExposureTargetsTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Only successful publication swaps stable buffers; disposing the owner deletes all owned storage.</summary>
    [Fact]
    public void PublicationSwapsDistinctPersistentHistoryBuffers()
    {
        EnsureContextValid();
        var targets = new CameraExposureTargets();
        int histogram = targets.Histogram.BufferId;
        int original = targets.Exposure.BufferId;
        int pending = targets.WriteTarget.BufferId;
        try
        {
            Assert.NotEqual(original, pending);
            Assert.Equal(CameraExposureTargets.HistogramGroupCount*512, targets.Histogram.SizeBytes);
            Assert.Equal(32, targets.Exposure.SizeBytes);

            targets.WriteTarget.UploadData(new[] { 2f, -12f, -12f, 1f, 3f, 1f, 0f, 0f });
            Assert.Equal(original, targets.Exposure.BufferId);
            targets.Publish();
            Assert.Equal(pending, targets.Exposure.BufferId);
            Assert.Equal(original, targets.WriteTarget.BufferId);
            Assert.Equal(histogram, targets.Histogram.BufferId);
            targets.Publish();
            Assert.Equal(original, targets.Exposure.BufferId);
            Assert.Equal(pending, targets.WriteTarget.BufferId);
            AssertNoGLError("exposure history publication");
        }
        finally { targets.Dispose(); }
        Assert.False(GL.IsBuffer(histogram));
        Assert.False(GL.IsBuffer(original));
        Assert.False(GL.IsBuffer(pending));
    }
    #endregion
}
