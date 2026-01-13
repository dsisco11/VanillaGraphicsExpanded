using System;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Rendering.Profiling;

public readonly struct GlGpuProfilerScope : IDisposable
{
    private readonly GlGpuProfiler? profiler;
    private readonly int token;
    private readonly GlDebug.GroupScope group;

    internal GlGpuProfilerScope(GlGpuProfiler profiler, int token, GlDebug.GroupScope group)
    {
        this.profiler = profiler;
        this.token = token;
        this.group = group;
    }

    public void Dispose()
    {
        profiler?.EndEvent(token);
        group.Dispose();
    }
}
