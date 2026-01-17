using System;
using System.Diagnostics.Tracing;

namespace VanillaGraphicsExpanded.Profiling;

/// <summary>
/// UE-style scoped CPU profiling markers backed by .NET EventSource.
/// </summary>
public static class Profiler
{
    public static bool IsEnabled => VgeProfilingEventSource.Log.IsEnabled();

    public static ProfileScope BeginScope(string name) => BeginScope(name, category: string.Empty);

    public static ProfileScope BeginScope(string name, string? category)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return default;
        }

        var log = VgeProfilingEventSource.Log;
        if (!log.IsEnabled(EventLevel.Informational, VgeProfilingEventSource.Keywords.CpuScopes))
        {
            return default;
        }

        string safeCategory = category ?? string.Empty;
        int threadId = Environment.CurrentManagedThreadId;
        long id = log.NextScopeId();

        log.ScopeStart(id, name, safeCategory, threadId);
        return new ProfileScope(id, threadId);
    }
}
