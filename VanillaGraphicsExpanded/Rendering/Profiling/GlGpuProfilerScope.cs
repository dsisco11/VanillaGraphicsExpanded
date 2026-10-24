using System;

namespace VanillaGraphicsExpanded.Rendering.Profiling;

/// <summary>Ends a debug GPU timing interval; Release scopes are empty value types.</summary>
public readonly struct GlGpuProfilerScope : IDisposable
{
    #region Public API
    /// <summary>Completes debug timing and grouping; Release disposal performs no work.</summary>
    public void Dispose()
    {
#if DEBUG
        profiler?.EndEvent(token);
        group.Dispose();
#endif
    }
    #endregion

#if DEBUG
    private readonly GlGpuProfiler? profiler;
    private readonly int token;
    private readonly GlDebug.GroupScope group;

    #region Private
    /// <summary>Retains the debug event and optional native debug group until disposal.</summary>
    internal GlGpuProfilerScope(GlGpuProfiler profiler, int token, GlDebug.GroupScope group)
    {
        this.profiler = profiler;
        this.token = token;
        this.group = group;
    }
    #endregion
#endif


}
