using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Profiling;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the shared profiler API against its Debug measurement and Release no-op contracts.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuProfilerConfigurationTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Conditional void calls omit their argument evaluation in ordinary Release builds.</summary>
    [Fact]
    public void ConditionalCallsOmitReleaseArguments()
    {
        var profiler = GlGpuProfiler.Instance;
        bool enabled = profiler.Enabled;
        profiler.Enabled = false;
        try
        {
            int evaluations = 0;
            profiler.BeginFrame(++evaluations, ++evaluations);
#if DEBUG
            Assert.Equal(2, evaluations);
#else
            Assert.Equal(0, evaluations);
#endif
        }
        finally { profiler.Enabled = enabled; }
    }

    /// <summary>The same using scope records real Debug timing and reports unavailable measurements in Release.</summary>
    [Fact]
    public void ScopeMeasuresOnlyInDebug()
    {
        EnsureContextValid();
        var profiler = GlGpuProfiler.Instance;
        bool enabled = profiler.Enabled;
        profiler.Dispose();
        profiler.Enabled = true;
        try
        {
            using var timestamps = new TimestampResultCapture();
            profiler.BeginFrame(1, 1);
            using (profiler.Scope("ProfilerConfiguration")) { }
            GL.Finish();
            profiler.Collect();
            output.WriteLine($"Completed timestamp reads: {string.Join(';', timestamps.Results)}");
#if DEBUG
            Assert.True(profiler.TryGetStats("ProfilerConfiguration", out var stats));
            Assert.Equal(1, stats.SampleCount);
            Assert.Single(profiler.GetSnapshot());
#else
            Assert.False(profiler.Enabled);
            Assert.False(profiler.TryGetStats("ProfilerConfiguration", out _));
            Assert.Empty(profiler.GetSnapshot());
#endif
            // Interface disposal must remain valid although Conditional cannot decorate an interface implementation.
            ((IDisposable)profiler).Dispose();
            Assert.Empty(profiler.GetSnapshot());
        }
        finally { profiler.Dispose(); profiler.Enabled = enabled; }
    }

    /// <summary>Counts equal completed timestamps as one zero-duration sample and rejects reversed intervals.</summary>
    [Theory]
    [InlineData(123456789UL, 123456789UL, 1)]
    [InlineData(123456790UL, 123456789UL, 0)]
    public void CompletedTimestampIntervalsPreserveZeroAndRejectReversedTime(ulong begin, ulong end, int expectedSamples)
    {
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
