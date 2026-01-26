using System;
using System.Threading;

namespace VanillaGraphicsExpanded.Cache.Disk;

/// <summary>
/// Schedules a single debounced flush action.
/// Multiple requests within the debounce window coalesce into one flush.
/// Intended for non-critical metadata writes (e.g., access-time touches).
/// </summary>
internal sealed class DebouncedFlushScheduler : IDisposable
{
    private readonly Action flushAction;
    private readonly TimeSpan delay;

    private readonly Timer timer;
    private int flushRunning;
    private int pending;
    private int disposed;

    public DebouncedFlushScheduler(Action flushAction, TimeSpan delay)
    {
        this.flushAction = flushAction ?? throw new ArgumentNullException(nameof(flushAction));
        this.delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;

        timer = new Timer(_ => OnTimer(), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Request()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref pending, 1);

        try
        {
            timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
        catch
        {
            // Best-effort.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            timer.Dispose();
        }
        catch
        {
            // Best-effort.
        }
    }

    private void OnTimer()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref flushRunning, 1) != 0)
        {
            return;
        }

        try
        {
            if (Interlocked.Exchange(ref pending, 0) == 0)
            {
                return;
            }

            try
            {
                flushAction();
            }
            catch
            {
                // Best-effort.
            }
        }
        finally
        {
            Interlocked.Exchange(ref flushRunning, 0);

            // If more work arrived while flushing, schedule again.
            if (Volatile.Read(ref pending) != 0 && Volatile.Read(ref disposed) == 0)
            {
                try
                {
                    timer.Change(delay, Timeout.InfiniteTimeSpan);
                }
                catch
                {
                    // Best-effort.
                }
            }
        }
    }
}
