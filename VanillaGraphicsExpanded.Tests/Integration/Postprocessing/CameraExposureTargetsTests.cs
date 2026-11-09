using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.CameraExposure;

namespace VanillaGraphicsExpanded.Tests.Integration.Postprocessing;

/// <summary>Checks temporal exposure publication never aliases its sampled history and retires owned storage.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CameraExposureTargetsTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Only successful publication swaps stable buffers; disposing the owner deletes all owned storage.</summary>
    [Fact]
    public void PublicationSwapsDistinctPersistentHistoryBuffers()
    {
        // Arrange: Create the exposure owner and retain the published read, write, and histogram buffer identities.
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

            // Act: Publish successive exposure results so the persistent read/write roles alternate.
            targets.WriteTarget.UploadData(new[] { 2f, -12f, -12f, 1f, 3f, 1f, 0f, 0f });
            Assert.Equal(original, targets.Exposure.BufferId);
            targets.Publish();
            Assert.Equal(pending, targets.Exposure.BufferId);
            Assert.Equal(original, targets.WriteTarget.BufferId);
            Assert.Equal(histogram, targets.Histogram.BufferId);
            targets.Publish();
            Assert.Equal(original, targets.Exposure.BufferId);
            Assert.Equal(pending, targets.WriteTarget.BufferId);
            // Assert: Only successful publications swap distinct buffers; owner disposal retires every allocation.
            AssertNoGLError("exposure history publication");
        }
        finally { targets.Dispose(); }
        Assert.False(GL.IsBuffer(histogram));
        Assert.False(GL.IsBuffer(original));
        Assert.False(GL.IsBuffer(pending));
    }
    #endregion
}
