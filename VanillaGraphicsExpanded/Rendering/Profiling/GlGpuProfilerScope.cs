using System;

namespace VanillaGraphicsExpanded.Rendering.Profiling;

public readonly struct GlGpuProfilerScope : IDisposable
{
    private readonly GlGpuProfiler? profiler;
    private readonly int token;

    internal GlGpuProfilerScope(GlGpuProfiler profiler, int token)
    {
        this.profiler = profiler;
        this.token = token;
    }

    public void Dispose()
    {
        profiler?.EndEvent(token);
    }
}
