using System;

namespace VanillaGraphicsExpanded.Profiling;

public readonly struct ProfileScope : IDisposable
{
    private readonly long id;
    private readonly int threadId;

    internal ProfileScope(long id, int threadId)
    {
        this.id = id;
        this.threadId = threadId;
    }

    public void Dispose()
    {
        if (id == 0)
        {
            return;
        }

        VgeProfilingEventSource.Log.ScopeStop(id, threadId);
    }
}
