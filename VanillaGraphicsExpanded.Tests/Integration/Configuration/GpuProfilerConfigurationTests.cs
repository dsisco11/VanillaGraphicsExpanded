using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Profiling;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Configuration;

/// <summary>Checks the shared profiler API against its Debug measurement and Release no-op contracts.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuProfilerConfigurationTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API


    /// <summary>The same using scope records real Debug timing and reports unavailable measurements in Release.</summary>
    [Fact]
    public void ScopeMeasuresOnlyInDebug()
    {
        // Arrange: Enable the shared profiler and capture timestamp reads while preserving its prior setting.
        EnsureContextValid();
        var profiler = GlGpuProfiler.Instance;
        bool enabled = profiler.Enabled;
        profiler.Dispose();
        profiler.Enabled = true;
        try
        {
            using var timestamps = new TimestampResultCapture();
            // Act: Record one frame-scoped measurement and collect its completed GPU query.
            profiler.BeginFrame(1, 1);
            using (profiler.Scope("ProfilerConfiguration")) { }
            GL.Finish();
            profiler.Collect();
            output.WriteLine($"Completed timestamp reads: {string.Join(';', timestamps.Results)}");
            // Assert: Debug publishes the sample; Release keeps the documented no-op result.
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

    #endregion
}
