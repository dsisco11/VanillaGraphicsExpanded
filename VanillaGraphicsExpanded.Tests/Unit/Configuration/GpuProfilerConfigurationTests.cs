using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Profiling;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Unit.Configuration;

/// <summary>Checks the shared profiler API against its Debug measurement and Release no-op contracts.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuProfilerConfigurationTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Conditional void calls omit their argument evaluation in ordinary Release builds.</summary>
    [Fact]
    public void ConditionalCallsOmitReleaseArguments()
    {
        // Arrange: Disable the profiler while preserving its prior state for cleanup.
        var profiler = GlGpuProfiler.Instance;
        bool enabled = profiler.Enabled;
        profiler.Enabled = false;
        try
        {
            int evaluations = 0;
            // Act: Pass side-effecting arguments through the conditional profiler API.
            profiler.BeginFrame(++evaluations, ++evaluations);
            // Assert: Debug evaluates both arguments; the Release no-op evaluates neither.
#if DEBUG
            Assert.Equal(2, evaluations);
#else
            Assert.Equal(0, evaluations);
#endif
        }
        finally { profiler.Enabled = enabled; }
    }


    #endregion
}
