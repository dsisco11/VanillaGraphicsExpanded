using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Profiling;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Configuration;

/// <summary>Checks the shared profiler API against its Debug measurement and Release no-op contracts.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuProfilerConfigurationTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API



    /// <summary>Counts equal completed timestamps as one zero-duration sample and rejects reversed intervals.</summary>
    [Theory]
    [InlineData(123456789UL, 123456789UL, 1)]
    [InlineData(123456790UL, 123456789UL, 0)]
    public void CompletedTimestampIntervalsPreserveZeroAndRejectReversedTime(ulong begin, ulong end, int expectedSamples)
    {
        // Arrange: Select equal or reversed timestamp endpoints and the expected completed-sample count.
        // Act and assert: Complete the profiler interval and verify zero-duration samples survive while reversed time is rejected.
        EnsureContextValid();
        var profiler = GlGpuProfiler.Instance;
        bool enabled = profiler.Enabled;
        profiler.Dispose();
        profiler.Enabled = true;
        try
        {
            using var timestamps = new TimestampResultCapture(begin, end);
            profiler.BeginFrame(1, 1);
            using (profiler.Scope("CompletedTimestampInterval")) { }
            GL.Finish();
            profiler.Collect();
#if DEBUG
            Assert.Equal(2, timestamps.Results.Count);
            Assert.Equal(2, timestamps.Results.Select(result => result.Query).Distinct().Count());
            Assert.Equal(new[] { begin, end }, timestamps.Results.Select(result => result.Returned));
            Assert.True(profiler.TryGetStats("CompletedTimestampInterval", out var stats));
            Assert.Equal(expectedSamples, stats.SampleCount);
            Assert.Equal(0, stats.LastMs);
            Assert.Equal(0, stats.AvgMs);
            Assert.Equal(0, stats.MinMs);
            Assert.Equal(0, stats.MaxMs);
#else
            Assert.Empty(timestamps.Results);
            Assert.False(profiler.Enabled);
            Assert.False(profiler.TryGetStats("CompletedTimestampInterval", out _));
            Assert.Empty(profiler.GetSnapshot());
#endif
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { profiler.Dispose(); profiler.Enabled = enabled; }
    }
    #endregion
}
